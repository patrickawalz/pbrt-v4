// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// shapes.h / shapes.cpp

using System;
using System.Collections.Generic;

namespace pbrt
{

    // ShapeSample Definition
    public unsafe struct ShapeSample
    {
        public ShapeSample(Interaction intr, Float pdf) { this.intr = intr; this.pdf = pdf; }
        public override string ToString()
        {
            return Globals.StringPrintf("[ ShapeSample intr: %s pdf: %f ]", intr, pdf);
        }

        public Interaction intr;
        public Float pdf;
    }

    // ShapeSampleContext Definition
    public unsafe struct ShapeSampleContext
    {
        // ShapeSampleContext Public Methods
        public ShapeSampleContext(Point3fi pi, Normal3f n, Normal3f ns, Float time)
        {
            this.pi = pi; this.n = n; this.ns = ns; this.time = time;
        }
        public ShapeSampleContext(SurfaceInteraction si)
        {
            pi = si.pi; n = si.n; ns = si.shading.n; time = si.time;
        }
        public ShapeSampleContext(MediumInteraction mi)
        {
            pi = mi.pi; n = default; ns = default; time = mi.time;
        }

        public Point3f p() { return (Point3f)pi; }

        // ShapeSampleContext Inline Methods
        public Point3f OffsetRayOrigin(Vector3f w)
        {
            // Find vector _offset_ to corner of error bounds and compute initial _po_
            Float d = Globals.Dot(Globals.Abs(n), pi.Error());
            Vector3f offset = d * (Vector3f)n;
            if (Globals.Dot(w, n) < 0)
                offset = -offset;
            Point3f po = (Point3f)pi + offset;

            // Round offset point _po_ away from _p_
            for (int i = 0; i < 3; ++i)
            {
                if (offset[i] > 0)
                    po[i] = Globals.NextFloatUp(po[i]);
                else if (offset[i] < 0)
                    po[i] = Globals.NextFloatDown(po[i]);
            }

            return po;
        }

        public Point3f OffsetRayOrigin(Point3f pt) { return OffsetRayOrigin(pt - p()); }

        public Ray SpawnRay(Vector3f w)
        {
            // Note: doesn't set medium, but that's fine, since this is only
            // used by shapes to see if ray would have intersected them
            return new Ray(OffsetRayOrigin(w), w, time);
        }

        public Point3fi pi;
        public Normal3f n, ns;
        public Float time;
    }

    // ShapeIntersection Definition
    public unsafe struct ShapeIntersection
    {
        public ShapeIntersection(SurfaceInteraction intr, Float tHit) { this.intr = intr; this.tHit = tHit; }
        public override string ToString()
        {
            return Globals.StringPrintf("[ ShapeIntersection intr: %s tHit: %f ]", intr, tHit);
        }

        public SurfaceInteraction intr;
        public Float tHit;
    }

    // QuadricIntersection Definition
    public unsafe struct QuadricIntersection
    {
        public QuadricIntersection(Float tHit, Point3f pObj, Float phi) { this.tHit = tHit; this.pObj = pObj; this.phi = phi; }
        public Float tHit;
        public Point3f pObj;
        public Float phi;
    }

    // Shared helpers for the shape implementations below
    internal static unsafe class ShapeUtil
    {
        public static Vector3f ToVector3f(Vector3fi v) { return new Vector3f((Float)v.x, (Float)v.y, (Float)v.z); }
        public static Point3f ToPoint3f(Point3fi p) { return new Point3f((Float)p.x, (Float)p.y, (Float)p.z); }

        // DifferenceOfProducts for vector/normal types, componentwise as in pbrt's templates
        public static Vector3f DifferenceOfProducts(Float a, Vector3f b, Float c, Vector3f d)
        {
            return new Vector3f(Globals.DifferenceOfProducts(a, b.x, c, d.x),
                                Globals.DifferenceOfProducts(a, b.y, c, d.y),
                                Globals.DifferenceOfProducts(a, b.z, c, d.z));
        }
        public static Normal3f DifferenceOfProducts(Float a, Normal3f b, Float c, Normal3f d)
        {
            return new Normal3f(Globals.DifferenceOfProducts(a, b.x, c, d.x),
                                Globals.DifferenceOfProducts(a, b.y, c, d.y),
                                Globals.DifferenceOfProducts(a, b.z, c, d.z));
        }

        // Common area-sampling to solid-angle conversion used by several shapes
        public static pstd.optional<ShapeSample> SampleByArea(Shape shape, ShapeSampleContext ctx, Point2f u)
        {
            // Sample shape by area and compute incident direction _wi_
            pstd.optional<ShapeSample> ss = shape.Sample(u);
            Globals.DCHECK(ss);
            ss.Ref().intr.time = ctx.time;
            Vector3f wi = ss.value().intr.p() - ctx.p();
            if (Globals.LengthSquared(wi) == 0)
                return default;
            wi = Globals.Normalize(wi);

            // Convert area sampling PDF in _ss_ to solid angle measure
            ss.Ref().pdf /= Globals.AbsDot(ss.value().intr.n, -wi) /
                            Globals.DistanceSquared(ctx.p(), ss.value().intr.p());
            if (Globals.IsInf(ss.value().pdf))
                return default;
            return ss;
        }

        public static Float PDFByArea(Shape shape, ShapeSampleContext ctx, Vector3f wi)
        {
            // Intersect sample ray with shape geometry
            Ray ray = ctx.SpawnRay(wi);
            pstd.optional<ShapeIntersection> isect = shape.Intersect(ray);
            if (!isect)
                return 0;

            // Compute PDF in solid angle measure from shape intersection point
            Float pdf = (1 / shape.Area()) / (Globals.AbsDot(isect.value().intr.n, -wi) /
                                              Globals.DistanceSquared(ctx.p(), isect.value().intr.p()));
            if (Globals.IsInf(pdf))
                pdf = 0;
            return pdf;
        }
    }

    // Sphere Definition
    public sealed unsafe class Sphere : Shape
    {
        // Sphere Public Methods
        public Sphere(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation, Float radius,
                      Float zMin, Float zMax, Float phiMax)
        {
            this.renderFromObject = renderFromObject;
            this.objectFromRender = objectFromRender;
            this.reverseOrientation = reverseOrientation;
            this.transformSwapsHandedness = renderFromObject.SwapsHandedness();
            this.radius = radius;
            this.zMin = Globals.Clamp(std.min(zMin, zMax), -radius, radius);
            this.zMax = Globals.Clamp(std.max(zMin, zMax), -radius, radius);
            this.thetaZMin = std.acos(Globals.Clamp(std.min(zMin, zMax) / radius, -1, 1));
            this.thetaZMax = std.acos(Globals.Clamp(std.max(zMin, zMax) / radius, -1, 1));
            this.phiMax = Globals.Radians(Globals.Clamp(phiMax, 0, 360));
        }

        public static Sphere Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation,
                                    ParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            Float radius = parameters.GetOneFloat("radius", 1.0f);
            Float zmin = parameters.GetOneFloat("zmin", -radius);
            Float zmax = parameters.GetOneFloat("zmax", radius);
            Float phimax = parameters.GetOneFloat("phimax", 360.0f);
            return new Sphere(renderFromObject, objectFromRender, reverseOrientation, radius, zmin, zmax, phimax);
        }

        public override string ToString()
        {
            return Globals.StringPrintf("[ Sphere renderFromObject: %s " +
                                        "objectFromRender: %s reverseOrientation: %s " +
                                        "transformSwapsHandedness: %s radius: %f zMin: %f " +
                                        "zMax: %f thetaMin: %f " +
                                        "thetaMax: %f phiMax: %f ]",
                                        renderFromObject, objectFromRender, reverseOrientation,
                                        transformSwapsHandedness, radius, zMin, zMax, thetaZMin,
                                        thetaZMax, phiMax);
        }

        public override Bounds3f Bounds()
        {
            return renderFromObject.Apply(
                new Bounds3f(new Point3f(-radius, -radius, zMin), new Point3f(radius, radius, zMax)));
        }

        public override DirectionCone NormalBounds() { return DirectionCone.EntireSphere(); }

        public override pstd.optional<ShapeIntersection> Intersect(Ray ray, Float tMax = Infinity)
        {
            pstd.optional<QuadricIntersection> isect = BasicIntersect(ray, tMax);
            if (!isect)
                return default;
            SurfaceInteraction intr = InteractionFromIntersection(isect.value(), -ray.d, ray.time);
            return new ShapeIntersection(intr, isect.value().tHit);
        }

        public pstd.optional<QuadricIntersection> BasicIntersect(Ray r, Float tMax)
        {
            Float phi;
            Point3f pHit;
            // Transform _Ray_ origin and direction to object space
            Point3fi oi = objectFromRender.Apply(new Point3fi(r.o));
            Vector3fi di = objectFromRender.Apply(new Vector3fi(r.d));

            // Solve quadratic equation to compute sphere _t0_ and _t1_
            Interval t0, t1;
            // Compute sphere quadratic coefficients
            Interval a = Globals.Sqr(di.x) + Globals.Sqr(di.y) + Globals.Sqr(di.z);
            Interval b = 2 * (di.x * oi.x + di.y * oi.y + di.z * oi.z);
            Interval c = Globals.Sqr(oi.x) + Globals.Sqr(oi.y) + Globals.Sqr(oi.z) - Globals.Sqr(new Interval(radius));

            // Compute sphere quadratic discriminant _discrim_
            Interval f = b / (2 * a);
            Vector3fi v = new Vector3fi(oi.x - f * di.x, oi.y - f * di.y, oi.z - f * di.z);
            Interval length = Globals.Length(v);
            Interval discrim = 4 * a * (new Interval(radius) + length) * (new Interval(radius) - length);
            if (discrim.LowerBound() < 0)
                return default;

            // Compute quadratic $t$ values
            Interval rootDiscrim = Globals.Sqrt(discrim);
            Interval q;
            if ((Float)b < 0)
                q = -.5f * (b - rootDiscrim);
            else
                q = -.5f * (b + rootDiscrim);
            t0 = q / a;
            t1 = c / q;
            // Swap quadratic $t$ values so that _t0_ is the lesser
            if (t0.LowerBound() > t1.LowerBound())
            {
                Interval tmp = t0; t0 = t1; t1 = tmp;
            }

            // Check quadric shape _t0_ and _t1_ for nearest intersection
            if (t0.UpperBound() > tMax || t1.LowerBound() <= 0)
                return default;
            Interval tShapeHit = t0;
            if (tShapeHit.LowerBound() <= 0)
            {
                tShapeHit = t1;
                if (tShapeHit.UpperBound() > tMax)
                    return default;
            }

            // Compute sphere hit position and $\phi$
            pHit = ShapeUtil.ToPoint3f(oi) + (Float)tShapeHit * ShapeUtil.ToVector3f(di);
            // Refine sphere intersection point
            pHit *= radius / Globals.Distance(pHit, new Point3f(0, 0, 0));

            if (pHit.x == 0 && pHit.y == 0)
                pHit.x = 1e-5f * radius;
            phi = std.atan2(pHit.y, pHit.x);
            if (phi < 0)
                phi += 2 * Pi;

            // Test sphere intersection against clipping parameters
            if ((zMin > -radius && pHit.z < zMin) || (zMax < radius && pHit.z > zMax) || phi > phiMax)
            {
                if (tShapeHit == t1)
                    return default;
                if (t1.UpperBound() > tMax)
                    return default;
                tShapeHit = t1;
                // Compute sphere hit position and $\phi$
                pHit = ShapeUtil.ToPoint3f(oi) + (Float)tShapeHit * ShapeUtil.ToVector3f(di);
                // Refine sphere intersection point
                pHit *= radius / Globals.Distance(pHit, new Point3f(0, 0, 0));

                if (pHit.x == 0 && pHit.y == 0)
                    pHit.x = 1e-5f * radius;
                phi = std.atan2(pHit.y, pHit.x);
                if (phi < 0)
                    phi += 2 * Pi;

                if ((zMin > -radius && pHit.z < zMin) || (zMax < radius && pHit.z > zMax) || phi > phiMax)
                    return default;
            }

            // Return _QuadricIntersection_ for sphere intersection
            return new QuadricIntersection((Float)tShapeHit, pHit, phi);
        }

        public override bool IntersectP(Ray r, Float tMax = Infinity) { return BasicIntersect(r, tMax); }

        public SurfaceInteraction InteractionFromIntersection(QuadricIntersection isect, Vector3f wo, Float time)
        {
            Point3f pHit = isect.pObj;
            Float phi = isect.phi;
            // Find parametric representation of sphere hit
            Float u = phi / phiMax;
            Float cosTheta = pHit.z / radius;
            Float theta = Globals.SafeACos(cosTheta);
            Float v = (theta - thetaZMin) / (thetaZMax - thetaZMin);
            // Compute sphere $\dpdu$ and $\dpdv$
            Float zRadius = std.sqrt(Globals.Sqr(pHit.x) + Globals.Sqr(pHit.y));
            Float cosPhi = pHit.x / zRadius, sinPhi = pHit.y / zRadius;
            Vector3f dpdu = new Vector3f(-phiMax * pHit.y, phiMax * pHit.x, 0);
            Float sinTheta = Globals.SafeSqrt(1 - Globals.Sqr(cosTheta));
            Vector3f dpdv = (thetaZMax - thetaZMin) *
                            new Vector3f(pHit.z * cosPhi, pHit.z * sinPhi, -radius * sinTheta);

            // Compute sphere $\dndu$ and $\dndv$
            Vector3f d2Pduu = -phiMax * phiMax * new Vector3f(pHit.x, pHit.y, 0);
            Vector3f d2Pduv = (thetaZMax - thetaZMin) * pHit.z * phiMax * new Vector3f(-sinPhi, cosPhi, 0.0f);
            Vector3f d2Pdvv = -Globals.Sqr(thetaZMax - thetaZMin) * new Vector3f(pHit.x, pHit.y, pHit.z);
            // Compute coefficients for fundamental forms
            Float E = Globals.Dot(dpdu, dpdu), F = Globals.Dot(dpdu, dpdv), G = Globals.Dot(dpdv, dpdv);
            Vector3f n = Globals.Normalize(Globals.Cross(dpdu, dpdv));
            Float e = Globals.Dot(n, d2Pduu), f = Globals.Dot(n, d2Pduv), g = Globals.Dot(n, d2Pdvv);

            // Compute $\dndu$ and $\dndv$ from fundamental form coefficients
            Float EGF2 = Globals.DifferenceOfProducts(E, G, F, F);
            Float invEGF2 = (EGF2 == 0) ? (Float)0 : 1 / EGF2;
            Normal3f dndu = (Normal3f)((f * F - e * G) * invEGF2 * dpdu + (e * F - f * E) * invEGF2 * dpdv);
            Normal3f dndv = (Normal3f)((g * F - f * G) * invEGF2 * dpdu + (f * F - g * E) * invEGF2 * dpdv);

            // Compute error bounds for sphere intersection
            Vector3f pError = Globals.gamma(5) * Globals.Abs((Vector3f)pHit);

            // Return _SurfaceInteraction_ for quadric intersection
            bool flipNormal = reverseOrientation ^ transformSwapsHandedness;
            Vector3f woObject = objectFromRender.Apply(wo);
            return renderFromObject.Apply(new SurfaceInteraction(new Point3fi(pHit, pError), new Point2f(u, v),
                                                                 woObject, dpdu, dpdv, dndu, dndv, time, flipNormal));
        }

        public override Float Area() { return phiMax * radius * (zMax - zMin); }

        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            Point3f pObj = new Point3f(0, 0, 0) + radius * Globals.SampleUniformSphere(u);
            // Reproject _pObj_ to sphere surface and compute _pObjError_
            pObj *= radius / Globals.Distance(pObj, new Point3f(0, 0, 0));
            Vector3f pObjError = Globals.gamma(5) * Globals.Abs((Vector3f)pObj);
            // Compute surface normal for sphere sample and return _ShapeSample_
            Normal3f nObj = new Normal3f(pObj.x, pObj.y, pObj.z);
            Normal3f n = Globals.Normalize(renderFromObject.Apply(nObj));
            if (reverseOrientation)
                n *= -1;

            // Compute $(u,v)$ coordinates for sphere sample
            Float theta = Globals.SafeACos(pObj.z / radius);
            Float phi = std.atan2(pObj.y, pObj.x);
            if (phi < 0)
                phi += 2 * Pi;
            Point2f uv = new Point2f(phi / phiMax, (theta - thetaZMin) / (thetaZMax - thetaZMin));

            Point3fi pi = renderFromObject.Apply(new Point3fi(pObj, pObjError));
            return new ShapeSample(new Interaction(pi, n, uv), 1 / Area());
        }

        public override Float PDF(Interaction intr) { return 1 / Area(); }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            // Sample uniformly on sphere if $\pt{}$ is inside it
            Point3f pCenter = renderFromObject.Apply(new Point3f(0, 0, 0));
            Point3f pOrigin = ctx.OffsetRayOrigin(pCenter);
            if (Globals.DistanceSquared(pOrigin, pCenter) <= Globals.Sqr(radius))
                return ShapeUtil.SampleByArea(this, ctx, u);

            // Sample sphere uniformly inside subtended cone
            // Compute quantities related to the $\theta_\roman{max}$ for cone
            Float sinThetaMax = radius / Globals.Distance(ctx.p(), pCenter);
            Float sin2ThetaMax = Globals.Sqr(sinThetaMax);
            Float cosThetaMax = Globals.SafeSqrt(1 - sin2ThetaMax);
            Float oneMinusCosThetaMax = 1 - cosThetaMax;

            // Compute $\theta$ and $\phi$ values for sample in cone
            Float cosTheta = (cosThetaMax - 1) * u[0] + 1;
            Float sin2Theta = 1 - Globals.Sqr(cosTheta);
            if (sin2ThetaMax < 0.00068523f /* sin^2(1.5 deg) */)
            {
                // Compute cone sample via Taylor series expansion for small angles
                sin2Theta = sin2ThetaMax * u[0];
                cosTheta = std.sqrt(1 - sin2Theta);
                oneMinusCosThetaMax = sin2ThetaMax / 2;
            }

            // Compute angle $\alpha$ from center of sphere to sampled point on surface
            Float cosAlpha = sin2Theta / sinThetaMax +
                             cosTheta * Globals.SafeSqrt(1 - sin2Theta / Globals.Sqr(sinThetaMax));
            Float sinAlpha = Globals.SafeSqrt(1 - Globals.Sqr(cosAlpha));

            // Compute surface normal and sampled point on sphere
            Float phi = u[1] * 2 * Pi;
            Vector3f w = Globals.SphericalDirection(sinAlpha, cosAlpha, phi);
            Frame samplingFrame = Frame.FromZ(Globals.Normalize(pCenter - ctx.p()));
            Normal3f n = (Normal3f)samplingFrame.FromLocal(-w);
            Point3f p = pCenter + radius * new Point3f(n.x, n.y, n.z);
            if (reverseOrientation)
                n *= -1;

            // Return _ShapeSample_ for sampled point on sphere
            // Compute _pError_ for sampled point on sphere
            Vector3f pError = Globals.gamma(5) * Globals.Abs((Vector3f)p);

            // Compute $(u,v)$ coordinates for sampled point on sphere
            Point3f pObj = objectFromRender.Apply(p);
            Float theta = Globals.SafeACos(pObj.z / radius);
            Float spherePhi = std.atan2(pObj.y, pObj.x);
            if (spherePhi < 0)
                spherePhi += 2 * Pi;
            Point2f uv = new Point2f(spherePhi / phiMax, (theta - thetaZMin) / (thetaZMax - thetaZMin));

            Globals.DCHECK_NE(oneMinusCosThetaMax, 0);  // very small far away sphere
            return new ShapeSample(new Interaction(new Point3fi(p, pError), n, ctx.time, uv),
                                   1 / (2 * Pi * oneMinusCosThetaMax));
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            Point3f pCenter = renderFromObject.Apply(new Point3f(0, 0, 0));
            Point3f pOrigin = ctx.OffsetRayOrigin(pCenter);
            if (Globals.DistanceSquared(pOrigin, pCenter) <= Globals.Sqr(radius))
                return ShapeUtil.PDFByArea(this, ctx, wi);  // Return solid angle PDF for point inside sphere

            // Compute general solid angle sphere PDF
            Float sin2ThetaMax = radius * radius / Globals.DistanceSquared(ctx.p(), pCenter);
            Float cosThetaMax = Globals.SafeSqrt(1 - sin2ThetaMax);
            Float oneMinusCosThetaMax = 1 - cosThetaMax;
            // Compute more accurate _oneMinusCosThetaMax_ for small solid angle
            if (sin2ThetaMax < 0.00068523f /* sin^2(1.5 deg) */)
                oneMinusCosThetaMax = sin2ThetaMax / 2;

            return 1 / (2 * Pi * oneMinusCosThetaMax);
        }

        // Sphere Private Members
        Float radius;
        Float zMin, zMax;
        Float thetaZMin, thetaZMax, phiMax;
        Transform renderFromObject, objectFromRender;
        bool reverseOrientation, transformSwapsHandedness;
    }

    // Disk Definition
    public sealed unsafe class Disk : Shape
    {
        // Disk Public Methods
        public Disk(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation, Float height,
                    Float radius, Float innerRadius, Float phiMax)
        {
            this.renderFromObject = renderFromObject;
            this.objectFromRender = objectFromRender;
            this.reverseOrientation = reverseOrientation;
            this.transformSwapsHandedness = renderFromObject.SwapsHandedness();
            this.height = height;
            this.radius = radius;
            this.innerRadius = innerRadius;
            this.phiMax = Globals.Radians(Globals.Clamp(phiMax, 0, 360));
        }

        public static Disk Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation,
                                  ParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            Float height = parameters.GetOneFloat("height", 0.0f);
            Float radius = parameters.GetOneFloat("radius", 1);
            Float innerRadius = parameters.GetOneFloat("innerradius", 0);
            Float phimax = parameters.GetOneFloat("phimax", 360);
            return new Disk(renderFromObject, objectFromRender, reverseOrientation, height, radius, innerRadius, phimax);
        }

        public override string ToString()
        {
            return Globals.StringPrintf(
                "[ Disk renderFromObject: %s objectFromRender: %s " +
                "reverseOrientation: %s " +
                "transformSwapsHandedness: %s height: %f radius: %f innerRadius: %f " +
                "phiMax: %f ]",
                renderFromObject, objectFromRender, reverseOrientation,
                transformSwapsHandedness, height, radius, innerRadius, phiMax);
        }

        public override Float Area() { return phiMax * 0.5f * (Globals.Sqr(radius) - Globals.Sqr(innerRadius)); }

        public override Bounds3f Bounds()
        {
            return renderFromObject.Apply(
                new Bounds3f(new Point3f(-radius, -radius, height), new Point3f(radius, radius, height)));
        }

        public override DirectionCone NormalBounds()
        {
            Normal3f n = renderFromObject.Apply(new Normal3f(0, 0, 1));
            if (reverseOrientation)
                n = -n;
            return new DirectionCone((Vector3f)n);
        }

        public override pstd.optional<ShapeIntersection> Intersect(Ray ray, Float tMax = Infinity)
        {
            pstd.optional<QuadricIntersection> isect = BasicIntersect(ray, tMax);
            if (!isect)
                return default;
            SurfaceInteraction intr = InteractionFromIntersection(isect.value(), -ray.d, ray.time);
            return new ShapeIntersection(intr, isect.value().tHit);
        }

        public pstd.optional<QuadricIntersection> BasicIntersect(Ray r, Float tMax)
        {
            // Transform _Ray_ origin and direction to object space
            Point3fi oi = objectFromRender.Apply(new Point3fi(r.o));
            Vector3fi di = objectFromRender.Apply(new Vector3fi(r.d));

            // Compute plane intersection for disk
            // Reject disk intersections for rays parallel to the disk's plane
            if ((Float)di.z == 0)
                return default;

            Float tShapeHit = (height - (Float)oi.z) / (Float)di.z;
            if (tShapeHit <= 0 || tShapeHit >= tMax)
                return default;

            // See if hit point is inside disk radii and $\phimax$
            Point3f pHit = ShapeUtil.ToPoint3f(oi) + (Float)tShapeHit * ShapeUtil.ToVector3f(di);
            Float dist2 = Globals.Sqr(pHit.x) + Globals.Sqr(pHit.y);
            if (dist2 > Globals.Sqr(radius) || dist2 < Globals.Sqr(innerRadius))
                return default;
            // Test disk $\phi$ value against $\phimax$
            Float phi = std.atan2(pHit.y, pHit.x);
            if (phi < 0)
                phi += 2 * Pi;
            if (phi > phiMax)
                return default;

            // Return _QuadricIntersection_ for disk intersection
            return new QuadricIntersection(tShapeHit, pHit, phi);
        }

        public SurfaceInteraction InteractionFromIntersection(QuadricIntersection isect, Vector3f wo, Float time)
        {
            Point3f pHit = isect.pObj;
            Float phi = isect.phi;
            // Find parametric representation of disk hit
            Float u = phi / phiMax;
            Float rHit = std.sqrt(Globals.Sqr(pHit.x) + Globals.Sqr(pHit.y));
            Float v = (radius - rHit) / (radius - innerRadius);
            Vector3f dpdu = new Vector3f(-phiMax * pHit.y, phiMax * pHit.x, 0);
            Vector3f dpdv = new Vector3f(pHit.x, pHit.y, 0) * (innerRadius - radius) / rHit;
            Normal3f dndu = new Normal3f(0, 0, 0), dndv = new Normal3f(0, 0, 0);

            // Refine disk intersection point
            pHit.z = height;

            // Compute error bounds for disk intersection
            Vector3f pError = new Vector3f(0, 0, 0);

            // Return _SurfaceInteraction_ for quadric intersection
            bool flipNormal = reverseOrientation ^ transformSwapsHandedness;
            Vector3f woObject = objectFromRender.Apply(wo);
            return renderFromObject.Apply(new SurfaceInteraction(new Point3fi(pHit, pError), new Point2f(u, v),
                                                                 woObject, dpdu, dpdv, dndu, dndv, time, flipNormal));
        }

        public override bool IntersectP(Ray r, Float tMax = Infinity) { return BasicIntersect(r, tMax); }

        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            Point2f pd = Globals.SampleUniformDiskConcentric(u);
            Point3f pObj = new Point3f(pd.x * radius, pd.y * radius, height);
            Point3fi pi = renderFromObject.Apply(new Point3fi(pObj));
            Normal3f n = Globals.Normalize(renderFromObject.Apply(new Normal3f(0, 0, 1)));
            if (reverseOrientation)
                n *= -1;
            // Compute $(u,v)$ for sampled point on disk
            Float phi = std.atan2(pd.y, pd.x);
            if (phi < 0)
                phi += 2 * Pi;
            Float radiusSample = std.sqrt(Globals.Sqr(pObj.x) + Globals.Sqr(pObj.y));
            Point2f uv = new Point2f(phi / phiMax, (radius - radiusSample) / (radius - innerRadius));
            return new ShapeSample(new Interaction(pi, n, uv), 1 / Area());
        }

        public override Float PDF(Interaction intr) { return 1 / Area(); }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            return ShapeUtil.SampleByArea(this, ctx, u);
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi) { return ShapeUtil.PDFByArea(this, ctx, wi); }

        // Disk Private Members
        Transform renderFromObject, objectFromRender;
        bool reverseOrientation, transformSwapsHandedness;
        Float height, radius, innerRadius, phiMax;
    }

    // Cylinder Definition
    public sealed unsafe class Cylinder : Shape
    {
        // Cylinder Public Methods
        public Cylinder(Transform renderFromObj, Transform objFromRender, bool reverseOrientation, Float radius,
                        Float zMin, Float zMax, Float phiMax)
        {
            this.renderFromObject = renderFromObj;
            this.objectFromRender = objFromRender;
            this.reverseOrientation = reverseOrientation;
            this.transformSwapsHandedness = renderFromObj.SwapsHandedness();
            this.radius = radius;
            this.zMin = std.min(zMin, zMax);
            this.zMax = std.max(zMin, zMax);
            this.phiMax = Globals.Radians(Globals.Clamp(phiMax, 0, 360));
        }

        public static Cylinder Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation,
                                      ParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            Float radius = parameters.GetOneFloat("radius", 1);
            Float zmin = parameters.GetOneFloat("zmin", -1);
            Float zmax = parameters.GetOneFloat("zmax", 1);
            Float phimax = parameters.GetOneFloat("phimax", 360);
            return new Cylinder(renderFromObject, objectFromRender, reverseOrientation, radius, zmin, zmax, phimax);
        }

        public override Bounds3f Bounds()
        {
            return renderFromObject.Apply(
                new Bounds3f(new Point3f(-radius, -radius, zMin), new Point3f(radius, radius, zMax)));
        }

        public override string ToString()
        {
            return Globals.StringPrintf("[ Cylinder renderFromObject: %s objectFromRender: %s " +
                                        "reverseOrientation: %s " +
                                        "transformSwapsHandedness: %s radius: %f zMin: %f zMax: %f " +
                                        "phiMax: %f ]",
                                        renderFromObject, objectFromRender, reverseOrientation,
                                        transformSwapsHandedness, radius, zMin, zMax, phiMax);
        }

        public override Float Area() { return (zMax - zMin) * radius * phiMax; }

        public override DirectionCone NormalBounds() { return DirectionCone.EntireSphere(); }

        public override pstd.optional<ShapeIntersection> Intersect(Ray ray, Float tMax = Infinity)
        {
            pstd.optional<QuadricIntersection> isect = BasicIntersect(ray, tMax);
            if (!isect)
                return default;
            SurfaceInteraction intr = InteractionFromIntersection(isect.value(), -ray.d, ray.time);
            return new ShapeIntersection(intr, isect.value().tHit);
        }

        public pstd.optional<QuadricIntersection> BasicIntersect(Ray r, Float tMax)
        {
            Float phi;
            Point3f pHit;
            // Transform _Ray_ origin and direction to object space
            Point3fi oi = objectFromRender.Apply(new Point3fi(r.o));
            Vector3fi di = objectFromRender.Apply(new Vector3fi(r.d));

            // Solve quadratic equation to find cylinder _t0_ and _t1_ values
            Interval t0, t1;
            // Compute cylinder quadratic coefficients
            Interval a = Globals.Sqr(di.x) + Globals.Sqr(di.y);
            Interval b = 2 * (di.x * oi.x + di.y * oi.y);
            Interval c = Globals.Sqr(oi.x) + Globals.Sqr(oi.y) - Globals.Sqr(new Interval(radius));

            // Compute cylinder quadratic discriminant _discrim_
            Interval f = b / (2 * a);
            Interval vx = oi.x - f * di.x, vy = oi.y - f * di.y;
            Interval length = Globals.Sqrt(Globals.Sqr(vx) + Globals.Sqr(vy));
            Interval discrim = 4 * a * (new Interval(radius) + length) * (new Interval(radius) - length);
            if (discrim.LowerBound() < 0)
                return default;

            // Compute quadratic $t$ values
            Interval rootDiscrim = Globals.Sqrt(discrim);
            Interval q;
            if ((Float)b < 0)
                q = -.5f * (b - rootDiscrim);
            else
                q = -.5f * (b + rootDiscrim);
            t0 = q / a;
            t1 = c / q;
            // Swap quadratic $t$ values so that _t0_ is the lesser
            if (t0.LowerBound() > t1.LowerBound())
            {
                Interval tmp = t0; t0 = t1; t1 = tmp;
            }

            // Check quadric shape _t0_ and _t1_ for nearest intersection
            if (t0.UpperBound() > tMax || t1.LowerBound() <= 0)
                return default;
            Interval tShapeHit = t0;
            if (tShapeHit.LowerBound() <= 0)
            {
                tShapeHit = t1;
                if (tShapeHit.UpperBound() > tMax)
                    return default;
            }

            // Compute cylinder hit point and $\phi$
            pHit = ShapeUtil.ToPoint3f(oi) + (Float)tShapeHit * ShapeUtil.ToVector3f(di);
            // Refine cylinder intersection point
            Float hitRad = std.sqrt(Globals.Sqr(pHit.x) + Globals.Sqr(pHit.y));
            pHit.x *= radius / hitRad;
            pHit.y *= radius / hitRad;

            phi = std.atan2(pHit.y, pHit.x);
            if (phi < 0)
                phi += 2 * Pi;

            // Test cylinder intersection against clipping parameters
            if (pHit.z < zMin || pHit.z > zMax || phi > phiMax)
            {
                if (tShapeHit == t1)
                    return default;
                tShapeHit = t1;
                if (t1.UpperBound() > tMax)
                    return default;
                // Compute cylinder hit point and $\phi$
                pHit = ShapeUtil.ToPoint3f(oi) + (Float)tShapeHit * ShapeUtil.ToVector3f(di);
                // Refine cylinder intersection point
                Float hitRad2 = std.sqrt(Globals.Sqr(pHit.x) + Globals.Sqr(pHit.y));
                pHit.x *= radius / hitRad2;
                pHit.y *= radius / hitRad2;

                phi = std.atan2(pHit.y, pHit.x);
                if (phi < 0)
                    phi += 2 * Pi;

                if (pHit.z < zMin || pHit.z > zMax || phi > phiMax)
                    return default;
            }

            // Return _QuadricIntersection_ for cylinder intersection
            return new QuadricIntersection((Float)tShapeHit, pHit, phi);
        }

        public override bool IntersectP(Ray r, Float tMax = Infinity) { return BasicIntersect(r, tMax); }

        public SurfaceInteraction InteractionFromIntersection(QuadricIntersection isect, Vector3f wo, Float time)
        {
            Point3f pHit = isect.pObj;
            Float phi = isect.phi;
            // Find parametric representation of cylinder hit
            Float u = phi / phiMax;
            Float v = (pHit.z - zMin) / (zMax - zMin);
            // Compute cylinder $\dpdu$ and $\dpdv$
            Vector3f dpdu = new Vector3f(-phiMax * pHit.y, phiMax * pHit.x, 0);
            Vector3f dpdv = new Vector3f(0, 0, zMax - zMin);

            // Compute cylinder $\dndu$ and $\dndv$
            Vector3f d2Pduu = -phiMax * phiMax * new Vector3f(pHit.x, pHit.y, 0);
            Vector3f d2Pduv = new Vector3f(0, 0, 0), d2Pdvv = new Vector3f(0, 0, 0);
            // Compute coefficients for fundamental forms
            Float E = Globals.Dot(dpdu, dpdu), F = Globals.Dot(dpdu, dpdv), G = Globals.Dot(dpdv, dpdv);
            Vector3f n = Globals.Normalize(Globals.Cross(dpdu, dpdv));
            Float e = Globals.Dot(n, d2Pduu), f = Globals.Dot(n, d2Pduv), g = Globals.Dot(n, d2Pdvv);

            // Compute $\dndu$ and $\dndv$ from fundamental form coefficients
            Float EGF2 = Globals.DifferenceOfProducts(E, G, F, F);
            Float invEGF2 = (EGF2 == 0) ? (Float)0 : 1 / EGF2;
            Normal3f dndu = (Normal3f)((f * F - e * G) * invEGF2 * dpdu + (e * F - f * E) * invEGF2 * dpdv);
            Normal3f dndv = (Normal3f)((g * F - f * G) * invEGF2 * dpdu + (f * F - g * E) * invEGF2 * dpdv);

            // Compute error bounds for cylinder intersection
            Vector3f pError = Globals.gamma(3) * Globals.Abs(new Vector3f(pHit.x, pHit.y, 0));

            // Return _SurfaceInteraction_ for quadric intersection
            bool flipNormal = reverseOrientation ^ transformSwapsHandedness;
            Vector3f woObject = objectFromRender.Apply(wo);
            return renderFromObject.Apply(new SurfaceInteraction(new Point3fi(pHit, pError), new Point2f(u, v),
                                                                 woObject, dpdu, dpdv, dndu, dndv, time, flipNormal));
        }

        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            Float z = Globals.Lerp(u[0], zMin, zMax);
            Float phi = u[1] * phiMax;
            // Compute cylinder sample position _pi_ and normal _n_ from $z$ and $\phi$
            Point3f pObj = new Point3f(radius * std.cos(phi), radius * std.sin(phi), z);
            // Reproject _pObj_ to cylinder surface and compute _pObjError_
            Float hitRad = std.sqrt(Globals.Sqr(pObj.x) + Globals.Sqr(pObj.y));
            pObj.x *= radius / hitRad;
            pObj.y *= radius / hitRad;
            Vector3f pObjError = Globals.gamma(3) * Globals.Abs(new Vector3f(pObj.x, pObj.y, 0));

            Point3fi pi = renderFromObject.Apply(new Point3fi(pObj, pObjError));
            Normal3f n = Globals.Normalize(renderFromObject.Apply(new Normal3f(pObj.x, pObj.y, 0)));
            if (reverseOrientation)
                n *= -1;
            // Compute $(u,v)$ for cylinder sample
            Point2f uv = new Point2f(phi / phiMax, (pObj.z - zMin) / (zMax - zMin));
            return new ShapeSample(new Interaction(pi, n, uv), 1 / Area());
        }

        public override Float PDF(Interaction intr) { return 1 / Area(); }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            return ShapeUtil.SampleByArea(this, ctx, u);
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi) { return ShapeUtil.PDFByArea(this, ctx, wi); }

        // Cylinder Private Members
        Transform renderFromObject, objectFromRender;
        bool reverseOrientation, transformSwapsHandedness;
        Float radius, zMin, zMax, phiMax;
    }

    // TriangleIntersection Definition
    public unsafe struct TriangleIntersection
    {
        public TriangleIntersection(Float b0, Float b1, Float b2, Float t) { this.b0 = b0; this.b1 = b1; this.b2 = b2; this.t = t; }
        public override string ToString()
        {
            return Globals.StringPrintf("[ TriangleIntersection b0: %f b1: %f b2: %f t: %f ]", b0, b1, b2, t);
        }

        public Float b0, b1, b2;
        public Float t;
    }

    // Triangle Definition
    public sealed unsafe class Triangle : Shape
    {
        // Triangle Public Methods
        public Triangle() { }
        public Triangle(int meshIndex, int triIndex) { this.meshIndex = meshIndex; this.triIndex = triIndex; }

        // STAT_PIXEL_RATIO("Intersections/Ray-Triangle intersection tests", nTriHits, nTriTests);
        [ThreadStatic] static long nTriHits, nTriTests;
        // STAT_MEMORY_COUNTER("Memory/Triangles", triangleBytes);
        [ThreadStatic] static long triangleBytes;
        static StatRegisterer STATS_REG = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportRatio("Intersections/Ray-Triangle intersection tests", nTriHits, nTriTests);
            nTriHits = nTriTests = 0;
            accum.ReportMemoryCounter("Memory/Triangles", triangleBytes);
            triangleBytes = 0;
        });

        static List<TriangleMesh> allMeshes;
        const Float MinSphericalSampleArea = 3e-4f;
        const Float MaxSphericalSampleArea = 6.22f;

        public static void Init(Allocator alloc)
        {
            allMeshes = new List<TriangleMesh>();
        }

        public static pstd.vector<Shape> CreateTriangles(TriangleMesh mesh, Allocator alloc)
        {
            int meshIndex;
            lock (allMeshesLock)
            {
                Globals.CHECK_LT(allMeshes.Count, 1 << 30);
                meshIndex = allMeshes.Count;
                allMeshes.Add(mesh);
            }

            pstd.vector<Shape> tris = new pstd.vector<Shape>((ulong)mesh.nTriangles);
            for (int i = 0; i < mesh.nTriangles; ++i)
                tris[i] = new Triangle(meshIndex, i);

            triangleBytes += mesh.nTriangles * 16;  // sizeof(Triangle) in pbrt-v4
            return tris;
        }
        static readonly object allMeshesLock = new object();

        public override Bounds3f Bounds()
        {
            // Get triangle vertices in _p0_, _p1_, and _p2_
            TriangleMesh mesh = GetMesh();
            int v0 = mesh.vertexIndices[3 * triIndex], v1 = mesh.vertexIndices[3 * triIndex + 1],
                v2 = mesh.vertexIndices[3 * triIndex + 2];
            Point3f p0 = mesh.p[v0], p1 = mesh.p[v1], p2 = mesh.p[v2];
            return Globals.Union(new Bounds3f(p0, p1), p2);
        }

        public override Float Area()
        {
            // Get triangle vertices in _p0_, _p1_, and _p2_
            TriangleMesh mesh = GetMesh();
            int v0 = mesh.vertexIndices[3 * triIndex], v1 = mesh.vertexIndices[3 * triIndex + 1],
                v2 = mesh.vertexIndices[3 * triIndex + 2];
            Point3f p0 = mesh.p[v0], p1 = mesh.p[v1], p2 = mesh.p[v2];
            return 0.5f * Globals.Length(Globals.Cross(p1 - p0, p2 - p0));
        }

        public override DirectionCone NormalBounds()
        {
            // Get triangle vertices in _p0_, _p1_, and _p2_
            TriangleMesh mesh = GetMesh();
            int v0 = mesh.vertexIndices[3 * triIndex], v1 = mesh.vertexIndices[3 * triIndex + 1],
                v2 = mesh.vertexIndices[3 * triIndex + 2];
            Point3f p0 = mesh.p[v0], p1 = mesh.p[v1], p2 = mesh.p[v2];

            Normal3f n = Globals.Normalize((Normal3f)Globals.Cross(p1 - p0, p2 - p0));
            // Ensure correct orientation of geometric normal for normal bounds
            if (mesh.n != null)
            {
                Normal3f ns = mesh.n[v0] + mesh.n[v1] + mesh.n[v2];
                n = Globals.FaceForward(n, ns);
            }
            else if (mesh.reverseOrientation ^ mesh.transformSwapsHandedness)
                n *= -1;

            return new DirectionCone((Vector3f)n);
        }

        public override pstd.optional<ShapeIntersection> Intersect(Ray ray, Float tMax = Infinity)
        {
            ++nTriTests;
            // Get triangle vertices in _p0_, _p1_, and _p2_
            TriangleMesh mesh = GetMesh();
            int v0 = mesh.vertexIndices[3 * triIndex], v1 = mesh.vertexIndices[3 * triIndex + 1],
                v2 = mesh.vertexIndices[3 * triIndex + 2];
            Point3f p0 = mesh.p[v0], p1 = mesh.p[v1], p2 = mesh.p[v2];

            pstd.optional<TriangleIntersection> triIsect = Globals.IntersectTriangle(ray, tMax, p0, p1, p2);
            if (!triIsect)
                return default;
            SurfaceInteraction intr = InteractionFromIntersection(mesh, triIndex, triIsect.value(), ray.time, -ray.d);
            ++nTriHits;
            return new ShapeIntersection(intr, triIsect.value().t);
        }

        public override bool IntersectP(Ray ray, Float tMax = Infinity)
        {
            ++nTriTests;
            // Get triangle vertices in _p0_, _p1_, and _p2_
            TriangleMesh mesh = GetMesh();
            int v0 = mesh.vertexIndices[3 * triIndex], v1 = mesh.vertexIndices[3 * triIndex + 1],
                v2 = mesh.vertexIndices[3 * triIndex + 2];
            Point3f p0 = mesh.p[v0], p1 = mesh.p[v1], p2 = mesh.p[v2];

            pstd.optional<TriangleIntersection> isect = Globals.IntersectTriangle(ray, tMax, p0, p1, p2);
            if (isect)
            {
                ++nTriHits;
                return true;
            }
            else
                return false;
        }

        public Float SolidAngle(Point3f p)
        {
            // Get triangle vertices in _p0_, _p1_, and _p2_
            TriangleMesh mesh = GetMesh();
            int v0 = mesh.vertexIndices[3 * triIndex], v1 = mesh.vertexIndices[3 * triIndex + 1],
                v2 = mesh.vertexIndices[3 * triIndex + 2];
            Point3f p0 = mesh.p[v0], p1 = mesh.p[v1], p2 = mesh.p[v2];
            return Globals.SphericalTriangleArea(Globals.Normalize(p0 - p), Globals.Normalize(p1 - p),
                                                 Globals.Normalize(p2 - p));
        }

        public override string ToString()
        {
            TriangleMesh mesh = GetMesh();
            int v0 = mesh.vertexIndices[3 * triIndex], v1 = mesh.vertexIndices[3 * triIndex + 1],
                v2 = mesh.vertexIndices[3 * triIndex + 2];
            Point3f p0 = mesh.p[v0];
            Point3f p1 = mesh.p[v1];
            Point3f p2 = mesh.p[v2];
            return Globals.StringPrintf("[ Triangle meshIndex: %d triIndex: %d -> p [ %s %s %s ] ]", meshIndex,
                                        triIndex, p0, p1, p2);
        }

        public static TriangleMesh CreateMesh(Transform renderFromObject, bool reverseOrientation,
                                              ParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            List<int> vi = parameters.GetIntArray("indices");
            List<Point3f> P = parameters.GetPoint3fArray("P");
            List<Point2f> uvs = parameters.GetPoint2fArray("uv");

            if (vi.Count == 0)
            {
                if (P.Count == 3)
                    vi = new List<int> { 0, 1, 2 };
                else
                {
                    Globals.Error(loc, "Vertex indices \"indices\" must be provided with " +
                                       "triangle mesh.");
                    return null;
                }
            }
            else if ((vi.Count % 3) != 0)
            {
                Globals.Error(loc, Globals.StringPrintf("Number of vertex indices %d not a multiple of 3. Discarding %d " +
                                                        "excess.", vi.Count, vi.Count % 3));
                while ((vi.Count % 3) != 0)
                    vi.RemoveAt(vi.Count - 1);
            }

            if (P.Count == 0)
            {
                Globals.Error(loc, "Vertex positions \"P\" must be provided with triangle mesh.");
                return null;
            }

            if (uvs.Count != 0 && uvs.Count != P.Count)
            {
                Globals.Error(loc, "Number of \"uv\"s for triangle mesh must match \"P\"s. " +
                                   "Discarding uvs.");
                uvs = new List<Point2f>();
            }

            List<Vector3f> S = parameters.GetVector3fArray("S");
            if (S.Count != 0 && S.Count != P.Count)
            {
                Globals.Error(loc, "Number of \"S\"s for triangle mesh must match \"P\"s. " +
                                   "Discarding \"S\"s.");
                S = new List<Vector3f>();
            }
            List<Normal3f> N = parameters.GetNormal3fArray("N");
            if (N.Count != 0 && N.Count != P.Count)
            {
                Globals.Error(loc, "Number of \"N\"s for triangle mesh must match \"P\"s. " +
                                   "Discarding \"N\"s.");
                N = new List<Normal3f>();
            }

            for (int i = 0; i < vi.Count; ++i)
                if (vi[i] >= P.Count)
                {
                    Globals.Error(loc, Globals.StringPrintf("trianglemesh has out of-bounds vertex index %d (%d \"P\" " +
                                                            "values were given. Discarding this mesh.",
                                                            vi[i], P.Count));
                    return null;
                }

            List<int> faceIndices = parameters.GetIntArray("faceIndices");
            if (faceIndices.Count != 0 && faceIndices.Count != vi.Count / 3)
            {
                Globals.Error(loc, Globals.StringPrintf("Number of face indices %d does not match number of triangles %d. " +
                                                        "Discarding face indices.", faceIndices.Count, vi.Count / 3));
                faceIndices = new List<int>();
            }

            return new TriangleMesh(renderFromObject, reverseOrientation, new pstd.vector<int>(vi),
                                    new pstd.vector<Point3f>(P), new pstd.vector<Vector3f>(S),
                                    new pstd.vector<Normal3f>(N), new pstd.vector<Point2f>(uvs),
                                    new pstd.vector<int>(faceIndices), alloc);
        }

        public static SurfaceInteraction InteractionFromIntersection(TriangleMesh mesh, int triIndex,
                                                                     TriangleIntersection ti, Float time, Vector3f wo)
        {
            int v0 = mesh.vertexIndices[3 * triIndex], v1 = mesh.vertexIndices[3 * triIndex + 1],
                v2 = mesh.vertexIndices[3 * triIndex + 2];
            Point3f p0 = mesh.p[v0], p1 = mesh.p[v1], p2 = mesh.p[v2];
            // Compute triangle partial derivatives
            // Compute deltas and matrix determinant for triangle partial derivatives
            // Get triangle texture coordinates in _uv_ array
            pstd.array3<Point2f> uv = mesh.uv != null
                                          ? new pstd.array3<Point2f>(mesh.uv[v0], mesh.uv[v1], mesh.uv[v2])
                                          : new pstd.array3<Point2f>(new Point2f(0, 0), new Point2f(1, 0), new Point2f(1, 1));
            Vector2f duv02 = uv[0] - uv[2], duv12 = uv[1] - uv[2];
            Vector3f dp02 = p0 - p2, dp12 = p1 - p2;
            Float determinant = Globals.DifferenceOfProducts(duv02[0], duv12[1], duv02[1], duv12[0]);

            Vector3f dpdu = default, dpdv = default;
            bool degenerateUV = std.abs(determinant) < 1e-9f;
            if (!degenerateUV)
            {
                // Compute triangle $\dpdu$ and $\dpdv$ via matrix inversion
                Float invdet = 1 / determinant;
                dpdu = ShapeUtil.DifferenceOfProducts(duv12[1], dp02, duv02[1], dp12) * invdet;
                dpdv = ShapeUtil.DifferenceOfProducts(duv02[0], dp12, duv12[0], dp02) * invdet;
            }
            // Handle degenerate triangle $(u,v)$ parameterization or partial derivatives
            if (degenerateUV || Globals.LengthSquared(Globals.Cross(dpdu, dpdv)) == 0)
            {
                Vector3f ng = Globals.Cross(p2 - p0, p1 - p0);
                if (Globals.LengthSquared(ng) == 0)
                {
                    // Vector3<double> fallback
                    double ax = (double)(p2.x - p0.x), ay = (double)(p2.y - p0.y), az = (double)(p2.z - p0.z);
                    double bx = (double)(p1.x - p0.x), by = (double)(p1.y - p0.y), bz = (double)(p1.z - p0.z);
                    ng = new Vector3f((Float)(ay * bz - az * by), (Float)(az * bx - ax * bz), (Float)(ax * by - ay * bx));
                    Globals.CHECK_NE(Globals.LengthSquared(ng), 0);
                }
                Vector3f a, b;
                Globals.CoordinateSystem(Globals.Normalize(ng), &a, &b);
                dpdu = a;
                dpdv = b;
            }

            // Interpolate $(u,v)$ parametric coordinates and hit point
            Point3f pHit = ti.b0 * p0 + ti.b1 * p1 + ti.b2 * p2;
            Point2f uvHit = ti.b0 * uv[0] + ti.b1 * uv[1] + ti.b2 * uv[2];

            // Return _SurfaceInteraction_ for triangle hit
            bool flipNormal = mesh.reverseOrientation ^ mesh.transformSwapsHandedness;
            // Compute error bounds _pError_ for triangle intersection
            Point3f pAbsSum = Globals.Abs(ti.b0 * p0) + Globals.Abs(ti.b1 * p1) + Globals.Abs(ti.b2 * p2);
            Vector3f pError = Globals.gamma(7) * (Vector3f)pAbsSum;
            SurfaceInteraction isect = new SurfaceInteraction(new Point3fi(pHit, pError), uvHit, wo, dpdu, dpdv,
                                                              new Normal3f(), new Normal3f(), time, flipNormal);
            // Compute norm of triangle...
            isect.faceIndex = mesh.faceIndices != null ? mesh.faceIndices[triIndex] : 0;

            // Set final surface normal and shading geometry for triangle
            // Override surface normal in _isect_ for triangle
            isect.n = isect.shading.n = (Normal3f)Globals.Normalize(Globals.Cross(dp02, dp12));
            if (mesh.reverseOrientation ^ mesh.transformSwapsHandedness)
                isect.n = isect.shading.n = -isect.n;

            if (mesh.n != null || mesh.s != null)
            {
                // Initialize _Triangle_ shading geometry
                // Compute shading normal _ns_ for triangle
                Normal3f ns;
                if (mesh.n != null)
                {
                    ns = ti.b0 * mesh.n[v0] + ti.b1 * mesh.n[v1] + ti.b2 * mesh.n[v2];
                    ns = Globals.LengthSquared(ns) > 0 ? Globals.Normalize(ns) : isect.n;
                }
                else
                    ns = isect.n;

                // Compute shading tangent _ss_ for triangle
                Vector3f ss;
                if (mesh.s != null)
                {
                    ss = ti.b0 * mesh.s[v0] + ti.b1 * mesh.s[v1] + ti.b2 * mesh.s[v2];
                    if (Globals.LengthSquared(ss) == 0)
                        ss = isect.dpdu;
                }
                else
                    ss = isect.dpdu;

                // Compute shading bitangent _ts_ for triangle and adjust _ss_
                Vector3f ts = Globals.Cross(ns, ss);
                if (Globals.LengthSquared(ts) > 0)
                    ss = Globals.Cross(ts, ns);
                else
                {
                    Vector3f a, b;
                    Globals.CoordinateSystem(ns, &a, &b);
                    ss = a;
                    ts = b;
                }

                // Compute $\dndu$ and $\dndv$ for triangle shading geometry
                Normal3f dndu, dndv;
                if (mesh.n != null)
                {
                    // Compute deltas for triangle partial derivatives of normal
                    Vector2f duv02b = uv[0] - uv[2];
                    Vector2f duv12b = uv[1] - uv[2];
                    Normal3f dn1 = mesh.n[v0] - mesh.n[v2];
                    Normal3f dn2 = mesh.n[v1] - mesh.n[v2];

                    Float determinantb = Globals.DifferenceOfProducts(duv02b[0], duv12b[1], duv02b[1], duv12b[0]);
                    bool degenerateUVb = std.abs(determinantb) < 1e-9;
                    if (degenerateUVb)
                    {
                        // We can still compute dndu and dndv, with respect to the
                        // same arbitrary coordinate system we use to compute dpdu
                        // and dpdv when this happens. It's important to do this
                        // (rather than giving up) so that ray differentials for
                        // rays reflected from triangles with degenerate
                        // parameterizations are still reasonable.
                        Vector3f dn = Globals.Cross((Vector3f)(mesh.n[v2] - mesh.n[v0]),
                                                    (Vector3f)(mesh.n[v1] - mesh.n[v0]));

                        if (Globals.LengthSquared(dn) == 0)
                            dndu = dndv = new Normal3f(0, 0, 0);
                        else
                        {
                            Vector3f dnu, dnv;
                            Globals.CoordinateSystem(dn, &dnu, &dnv);
                            dndu = (Normal3f)dnu;
                            dndv = (Normal3f)dnv;
                        }
                    }
                    else
                    {
                        Float invDet = 1 / determinantb;
                        dndu = ShapeUtil.DifferenceOfProducts(duv12b[1], dn1, duv02b[1], dn2) * invDet;
                        dndv = ShapeUtil.DifferenceOfProducts(duv02b[0], dn2, duv12b[0], dn1) * invDet;
                    }
                }
                else
                    dndu = dndv = new Normal3f(0, 0, 0);

                isect.SetShadingGeometry(ns, ss, ts, dndu, dndv, true);
            }

            return isect;
        }

        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            // Get triangle vertices in _p0_, _p1_, and _p2_
            TriangleMesh mesh = GetMesh();
            int v0 = mesh.vertexIndices[3 * triIndex], v1 = mesh.vertexIndices[3 * triIndex + 1],
                v2 = mesh.vertexIndices[3 * triIndex + 2];
            Point3f p0 = mesh.p[v0], p1 = mesh.p[v1], p2 = mesh.p[v2];

            // Sample point on triangle uniformly by area
            pstd.array3<Float> b = Globals.SampleUniformTriangle(u);
            Point3f p = b[0] * p0 + b[1] * p1 + b[2] * p2;

            // Compute surface normal for sampled point on triangle
            Normal3f n = Globals.Normalize((Normal3f)Globals.Cross(p1 - p0, p2 - p0));
            if (mesh.n != null)
            {
                Normal3f ns = b[0] * mesh.n[v0] + b[1] * mesh.n[v1] + (1 - b[0] - b[1]) * mesh.n[v2];
                n = Globals.FaceForward(n, ns);
            }
            else if (mesh.reverseOrientation ^ mesh.transformSwapsHandedness)
                n *= -1;

            // Compute $(u,v)$ for sampled point on triangle
            // Get triangle texture coordinates in _uv_ array
            pstd.array3<Point2f> uv = mesh.uv != null
                                          ? new pstd.array3<Point2f>(mesh.uv[v0], mesh.uv[v1], mesh.uv[v2])
                                          : new pstd.array3<Point2f>(new Point2f(0, 0), new Point2f(1, 0), new Point2f(1, 1));
            Point2f uvSample = b[0] * uv[0] + b[1] * uv[1] + b[2] * uv[2];

            // Compute error bounds _pError_ for sampled point on triangle
            Point3f pAbsSum = Globals.Abs(b[0] * p0) + Globals.Abs(b[1] * p1) + Globals.Abs((1 - b[0] - b[1]) * p2);
            Vector3f pError = (Vector3f)(Globals.gamma(6) * pAbsSum);

            return new ShapeSample(new Interaction(new Point3fi(p, pError), n, uvSample), 1 / Area());
        }

        public override Float PDF(Interaction intr) { return 1 / Area(); }

        static pstd.span<Float> W4(Float a, Float b, Float c, Float d)
        {
            return new pstd.span<Float>(new Float[] { a, b, c, d });
        }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            // Get triangle vertices in _p0_, _p1_, and _p2_
            TriangleMesh mesh = GetMesh();
            int v0 = mesh.vertexIndices[3 * triIndex], v1 = mesh.vertexIndices[3 * triIndex + 1],
                v2 = mesh.vertexIndices[3 * triIndex + 2];
            Point3f p0 = mesh.p[v0], p1 = mesh.p[v1], p2 = mesh.p[v2];

            // Use uniform area sampling for numerically unstable cases
            Float solidAngle = SolidAngle(ctx.p());
            if (solidAngle < MinSphericalSampleArea || solidAngle > MaxSphericalSampleArea)
                return ShapeUtil.SampleByArea(this, ctx, u);

            // Sample spherical triangle from reference point
            // Apply warp product sampling for cosine factor at reference point
            Float pdf = 1;
            if (ctx.ns != new Normal3f(0, 0, 0))
            {
                // Compute $\cos\theta$-based weights _w_ at sample domain corners
                Point3f rp = ctx.p();
                Vector3f wi0 = Globals.Normalize(p0 - rp), wi1 = Globals.Normalize(p1 - rp),
                         wi2 = Globals.Normalize(p2 - rp);
                pstd.span<Float> w = W4(std.max<Float>(0.01f, Globals.AbsDot(ctx.ns, wi1)),
                                        std.max<Float>(0.01f, Globals.AbsDot(ctx.ns, wi1)),
                                        std.max<Float>(0.01f, Globals.AbsDot(ctx.ns, wi0)),
                                        std.max<Float>(0.01f, Globals.AbsDot(ctx.ns, wi2)));
                u = Globals.SampleBilinear(u, w);
                Globals.DCHECK(u[0] >= 0 && u[0] < 1 && u[1] >= 0 && u[1] < 1);
                pdf = Globals.BilinearPDF(u, w);
            }

            Float triPDF;
            pstd.array3<Float> b = Globals.SampleSphericalTriangle(new pstd.array3<Point3f>(p0, p1, p2), ctx.p(), u,
                                                                   &triPDF);
            if (triPDF == 0)
                return default;
            pdf *= triPDF;

            // Compute error bounds _pError_ for sampled point on triangle
            Point3f pAbsSum = Globals.Abs(b[0] * p0) + Globals.Abs(b[1] * p1) + Globals.Abs((1 - b[0] - b[1]) * p2);
            Vector3f pError = (Vector3f)(Globals.gamma(6) * pAbsSum);

            // Return _ShapeSample_ for solid angle sampled point on triangle
            Point3f p = b[0] * p0 + b[1] * p1 + b[2] * p2;
            // Compute surface normal for sampled point on triangle
            Normal3f n = Globals.Normalize((Normal3f)Globals.Cross(p1 - p0, p2 - p0));
            if (mesh.n != null)
            {
                Normal3f ns = b[0] * mesh.n[v0] + b[1] * mesh.n[v1] + (1 - b[0] - b[1]) * mesh.n[v2];
                n = Globals.FaceForward(n, ns);
            }
            else if (mesh.reverseOrientation ^ mesh.transformSwapsHandedness)
                n *= -1;

            // Compute $(u,v)$ for sampled point on triangle
            // Get triangle texture coordinates in _uv_ array
            pstd.array3<Point2f> uv = mesh.uv != null
                                          ? new pstd.array3<Point2f>(mesh.uv[v0], mesh.uv[v1], mesh.uv[v2])
                                          : new pstd.array3<Point2f>(new Point2f(0, 0), new Point2f(1, 0), new Point2f(1, 1));
            Point2f uvSample = b[0] * uv[0] + b[1] * uv[1] + b[2] * uv[2];

            return new ShapeSample(new Interaction(new Point3fi(p, pError), n, ctx.time, uvSample), pdf);
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            Float solidAngle = SolidAngle(ctx.p());
            // Return PDF based on uniform area sampling for challenging triangles
            if (solidAngle < MinSphericalSampleArea || solidAngle > MaxSphericalSampleArea)
                return ShapeUtil.PDFByArea(this, ctx, wi);

            Float pdf = 1 / solidAngle;
            // Adjust PDF for warp product sampling of triangle $\cos\theta$ factor
            if (ctx.ns != new Normal3f(0, 0, 0))
            {
                // Get triangle vertices in _p0_, _p1_, and _p2_
                TriangleMesh mesh = GetMesh();
                int v0 = mesh.vertexIndices[3 * triIndex], v1 = mesh.vertexIndices[3 * triIndex + 1],
                    v2 = mesh.vertexIndices[3 * triIndex + 2];
                Point3f p0 = mesh.p[v0], p1 = mesh.p[v1], p2 = mesh.p[v2];

                Point2f u = Globals.InvertSphericalTriangleSample(new pstd.array3<Point3f>(p0, p1, p2), ctx.p(), wi);
                // Compute $\cos\theta$-based weights _w_ at sample domain corners
                Point3f rp = ctx.p();
                Vector3f wi0 = Globals.Normalize(p0 - rp), wi1 = Globals.Normalize(p1 - rp),
                         wi2 = Globals.Normalize(p2 - rp);
                pstd.span<Float> w = W4(std.max<Float>(0.01f, Globals.AbsDot(ctx.ns, wi1)),
                                        std.max<Float>(0.01f, Globals.AbsDot(ctx.ns, wi1)),
                                        std.max<Float>(0.01f, Globals.AbsDot(ctx.ns, wi0)),
                                        std.max<Float>(0.01f, Globals.AbsDot(ctx.ns, wi2)));
                pdf *= Globals.BilinearPDF(u, w);
            }

            return pdf;
        }

        // Triangle Private Methods
        TriangleMesh GetMesh() { return allMeshes[meshIndex]; }

        // Triangle Private Members
        int meshIndex = -1, triIndex = -1;
    }

    // CurveType Definition
    public enum CurveType { Flat, Cylinder, Ribbon }

    // CurveCommon Definition
    public sealed unsafe class CurveCommon
    {
        // CurveCommon Public Methods
        public CurveCommon(pstd.span<Point3f> c, Float width0, Float width1, CurveType type, pstd.span<Normal3f> norm,
                           Transform renderFromObject, Transform objectFromRender, bool reverseOrientation)
        {
            this.type = type;
            this.renderFromObject = renderFromObject;
            this.objectFromRender = objectFromRender;
            this.reverseOrientation = reverseOrientation;
            this.transformSwapsHandedness = renderFromObject.SwapsHandedness();
            width[0] = width0;
            width[1] = width1;
            Globals.CHECK_EQ((int)c.size(), 4);
            for (int i = 0; i < 4; ++i)
                cpObj[i] = c[i];

            if (norm.size() == 2)
            {
                n[0] = Globals.Normalize(norm[0]);
                n[1] = Globals.Normalize(norm[1]);
                normalAngle = Globals.AngleBetween(n[0], n[1]);
                invSinNormalAngle = 1 / std.sin(normalAngle);
            }
            ++Curve.nCurves;
        }

        public override string ToString()
        {
            return Globals.StringPrintf(
                "[ CurveCommon type: %s cpObj: [ %s %s %s %s ] width: [ %f %f ] n: [ %s %s ] normalAngle: %f " +
                "invSinNormalAngle: %f renderFromObject: %s " +
                "objectFromRender: %s " +
                "reverseOrientation: %s transformSwapsHandedness: %s ]",
                type.ToString(), cpObj[0], cpObj[1], cpObj[2], cpObj[3], width[0], width[1], n[0], n[1],
                normalAngle, invSinNormalAngle, renderFromObject, objectFromRender,
                reverseOrientation, transformSwapsHandedness);
        }

        // CurveCommon Public Members
        public CurveType type;
        public Point3f[] cpObj = new Point3f[4];
        public Float[] width = new Float[2];
        public Normal3f[] n = new Normal3f[2];
        public Float normalAngle, invSinNormalAngle;
        public Transform renderFromObject, objectFromRender;
        public bool reverseOrientation, transformSwapsHandedness;
    }

    // Curve Definition
    public sealed unsafe class Curve : Shape
    {
        // STAT_PERCENT("Intersections/Ray-curve intersection tests", nCurveHits, nCurveTests);
        // STAT_COUNTER("Geometry/Curves", nCurves);
        // STAT_COUNTER("Geometry/Split curves", nSplitCurves);
        // STAT_MEMORY_COUNTER("Memory/Curves", curveBytes);
        [ThreadStatic] static long nCurveHits, nCurveTests;
        [ThreadStatic] internal static long nCurves;
        [ThreadStatic] static long nSplitCurves;
        [ThreadStatic] static long curveBytes;
        static StatRegisterer STATS_REG = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportPercentage("Intersections/Ray-curve intersection tests", nCurveHits, nCurveTests);
            nCurveHits = nCurveTests = 0;
            accum.ReportCounter("Geometry/Curves", nCurves);
            nCurves = 0;
            accum.ReportCounter("Geometry/Split curves", nSplitCurves);
            nSplitCurves = 0;
            accum.ReportMemoryCounter("Memory/Curves", curveBytes);
            curveBytes = 0;
        });

        // Curve Public Methods
        public Curve(CurveCommon common, Float uMin, Float uMax)
        {
            this.common = common;
            this.uMin = uMin;
            this.uMax = uMax;
        }

        public static pstd.vector<Shape> CreateCurve(Transform renderFromObject, Transform objectFromRender,
                                                     bool reverseOrientation, pstd.span<Point3f> c, Float w0,
                                                     Float w1, CurveType type, pstd.span<Normal3f> norm,
                                                     int splitDepth, Allocator alloc)
        {
            CurveCommon common = new CurveCommon(c, w0, w1, type, norm, renderFromObject, objectFromRender,
                                                 reverseOrientation);
            int nSegments = 1 << splitDepth;
            pstd.vector<Shape> segments = new pstd.vector<Shape>((ulong)nSegments);
            for (int i = 0; i < nSegments; ++i)
            {
                Float uMin = i / (Float)nSegments;
                Float uMax = (i + 1) / (Float)nSegments;
                segments[i] = new Curve(common, uMin, uMax);
                ++nSplitCurves;
            }
            curveBytes += 144 + nSegments * 24;  // sizeof(CurveCommon) + nSegments * sizeof(Curve) in pbrt-v4
            return segments;
        }

        public static pstd.vector<Shape> Create(Transform renderFromObject, Transform objectFromRender,
                                                bool reverseOrientation, ParameterDictionary parameters,
                                                FileLoc loc, Allocator alloc)
        {
            Float width = parameters.GetOneFloat("width", 1.0f);
            Float width0 = parameters.GetOneFloat("width0", width);
            Float width1 = parameters.GetOneFloat("width1", width);

            int degree = parameters.GetOneInt("degree", 3);
            if (degree != 2 && degree != 3)
            {
                Globals.Error(loc, Globals.StringPrintf("Invalid degree %d: only degree 2 and 3 curves are supported.",
                                                        degree));
                return new pstd.vector<Shape>();
            }

            string basis = parameters.GetOneString("basis", "bezier");
            if (basis != "bezier" && basis != "bspline")
            {
                Globals.Error(loc, Globals.StringPrintf("Invalid basis \"%s\": only \"bezier\" and \"bspline\" are " +
                                                        "supported.", basis));
                return new pstd.vector<Shape>();
            }

            int nSegments;
            List<Point3f> cp = parameters.GetPoint3fArray("P");
            if (basis == "bezier")
            {
                // After the first segment, which uses degree+1 control points,
                // subsequent segments reuse the last control point of the previous
                // one and then use degree more control points.
                if (((cp.Count - 1 - degree) % degree) != 0)
                {
                    Globals.Error(loc, Globals.StringPrintf("Invalid number of control points %d: for the degree %d " +
                                                            "Bezier basis %d + n * %d are required, for n >= 0.",
                                                            cp.Count, degree, degree + 1, degree));
                    return new pstd.vector<Shape>();
                }
                nSegments = (cp.Count - 1) / degree;
            }
            else
            {
                if (cp.Count < degree + 1)
                {
                    Globals.Error(loc, Globals.StringPrintf("Invalid number of control points %d: for the degree %d " +
                                                            "b-spline basis, must have >= %d.",
                                                            cp.Count, degree, degree + 1));
                    return new pstd.vector<Shape>();
                }
                nSegments = cp.Count - degree;
            }

            CurveType type;
            string curveType = parameters.GetOneString("type", "flat");
            if (curveType == "flat")
                type = CurveType.Flat;
            else if (curveType == "ribbon")
                type = CurveType.Ribbon;
            else if (curveType == "cylinder")
                type = CurveType.Cylinder;
            else
            {
                Globals.Error(loc, Globals.StringPrintf("Unknown curve type \"%s\".  Using \"cylinder\".", curveType));
                type = CurveType.Cylinder;
            }

            List<Normal3f> n = parameters.GetNormal3fArray("N");
            if (n.Count != 0)
            {
                if (type != CurveType.Ribbon)
                {
                    Globals.Warning("Curve normals are only used with \"ribbon\" type curves.");
                    n = new List<Normal3f>();
                }
                else if (n.Count != nSegments + 1)
                {
                    Globals.Error(loc, Globals.StringPrintf("Invalid number of normals %d: must provide %d normals for " +
                                                            "ribbon curves with %d segments.",
                                                            n.Count, nSegments + 1, nSegments));
                    return new pstd.vector<Shape>();
                }
            }
            else if (type == CurveType.Ribbon)
            {
                Globals.Error(loc, "Must provide normals \"N\" at curve endpoints with ribbon " +
                                   "curves.");
                return new pstd.vector<Shape>();
            }

            int sd = Globals.Options.useGPU ? 0 : parameters.GetOneInt("splitdepth", 3);

            if (type == CurveType.Ribbon && n.Count == 0)
            {
                Globals.Error(loc, "Must provide normals \"N\" at curve endpoints with ribbon " +
                                   "curves.");
                return new pstd.vector<Shape>();
            }

            pstd.vector<Shape> curves = new pstd.vector<Shape>();
            Point3f[] cpArr = cp.ToArray();
            Normal3f[] nArr = n.ToArray();
            int cpOffset = 0;
            for (int seg = 0; seg < nSegments; ++seg)
            {
                pstd.array4<Point3f> segCpBezier;

                // Get control points for the current segment as a Bezier curve
                if (basis == "bezier")
                {
                    // Create elevated degree 3 Bezier from degree 2 control points
                    if (degree == 2)
                    {
                        segCpBezier = Globals.ElevateQuadraticBezierToCubic(new pstd.span<Point3f>(cpArr, cpOffset, 3));
                    }
                    else
                    {
                        segCpBezier = new pstd.array4<Point3f>();
                        for (int i = 0; i < 4; ++i)
                            segCpBezier[i] = cpArr[cpOffset + i];
                    }
                    cpOffset += degree;
                }
                else
                {
                    // Convert B-spline control points to Bezier
                    if (degree == 2)
                    {
                        pstd.array3<Point3f> bezCp =
                            Globals.QuadraticBSplineToBezier(new pstd.span<Point3f>(cpArr, cpOffset, 3));
                        segCpBezier = Globals.ElevateQuadraticBezierToCubic(
                            new pstd.span<Point3f>(new Point3f[] { bezCp[0], bezCp[1], bezCp[2] }));
                    }
                    else
                    {
                        segCpBezier = Globals.CubicBSplineToBezier(new pstd.span<Point3f>(cpArr, cpOffset, 4));
                    }
                    ++cpOffset;
                }

                pstd.span<Normal3f> nspan = default;
                if (n.Count != 0)
                    nspan = new pstd.span<Normal3f>(nArr, seg, 2);
                pstd.vector<Shape> c = CreateCurve(
                    renderFromObject, objectFromRender, reverseOrientation,
                    new pstd.span<Point3f>(new Point3f[] { segCpBezier[0], segCpBezier[1], segCpBezier[2], segCpBezier[3] }),
                    Globals.Lerp((Float)seg / (Float)nSegments, width0, width1),
                    Globals.Lerp((Float)(seg + 1) / (Float)nSegments, width0, width1), type, nspan, sd, alloc);
                for (int i = 0; i < (int)c.size(); ++i)
                    curves.push_back(c[i]);
            }
            return curves;
        }

        public override Bounds3f Bounds()
        {
            pstd.span<Point3f> cpSpan = new pstd.span<Point3f>(common.cpObj);
            Bounds3f objBounds = Globals.BoundCubicBezier(cpSpan, uMin, uMax);
            // Expand _objBounds_ by maximum curve width over $u$ range
            Float w0 = Globals.Lerp(uMin, common.width[0], common.width[1]);
            Float w1 = Globals.Lerp(uMax, common.width[0], common.width[1]);
            objBounds = Globals.Expand(objBounds, std.max(w0, w1) * 0.5f);

            return common.renderFromObject.Apply(objBounds);
        }

        public override DirectionCone NormalBounds() { return DirectionCone.EntireSphere(); }

        public override Float Area()
        {
            pstd.array4<Point3f> cpObj = Globals.CubicBezierControlPoints(new pstd.span<Point3f>(common.cpObj), uMin, uMax);
            Float width0 = Globals.Lerp(uMin, common.width[0], common.width[1]);
            Float width1 = Globals.Lerp(uMax, common.width[0], common.width[1]);
            Float avgWidth = (width0 + width1) * 0.5f;
            Float approxLength = 0.0f;
            for (int i = 0; i < 3; ++i)
                approxLength += Globals.Distance(cpObj[i], cpObj[i + 1]);
            return approxLength * avgWidth;
        }

        public override pstd.optional<ShapeIntersection> Intersect(Ray ray, Float tMax = Infinity)
        {
            pstd.optional<ShapeIntersection> si = default;
            IntersectRay(ray, tMax, &si);
            return si;
        }

        public override bool IntersectP(Ray ray, Float tMax = Infinity) { return IntersectRay(ray, tMax, null); }

        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            Globals.LOG_FATAL("Curve::Sample not implemented.");
            return default;
        }
        public override Float PDF(Interaction intr)
        {
            Globals.LOG_FATAL("Curve::PDF not implemented.");
            return 0;
        }
        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            Globals.LOG_FATAL("Curve::Sample not implemented.");
            return default;
        }
        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            Globals.LOG_FATAL("Curve::PDF not implemented.");
            return 0;
        }

        public override string ToString()
        {
            return Globals.StringPrintf("[ Curve common: %s uMin: %f uMax: %f ]", common, uMin, uMax);
        }

        // Curve Private Methods
        bool IntersectRay(Ray r, Float tMax, pstd.optional<ShapeIntersection>* si)
        {
            ++nCurveTests;
            // Transform _Ray_ to curve's object space
            Ray ray = common.objectFromRender.Apply(r);

            // Get object-space control points for curve segment, _cpObj_
            pstd.array4<Point3f> cpObj = Globals.CubicBezierControlPoints(new pstd.span<Point3f>(common.cpObj), uMin, uMax);

            // Project curve control points to plane perpendicular to ray
            Vector3f dx = Globals.Cross(ray.d, cpObj[3] - cpObj[0]);
            if (Globals.LengthSquared(dx) == 0)
            {
                Vector3f dy;
                Globals.CoordinateSystem(ray.d, &dx, &dy);
            }
            Transform rayFromObject = Globals.LookAt(ray.o, ray.o + ray.d, dx);
            Point3f[] cp = new Point3f[4] { rayFromObject.Apply(cpObj[0]), rayFromObject.Apply(cpObj[1]),
                                            rayFromObject.Apply(cpObj[2]), rayFromObject.Apply(cpObj[3]) };

            // Test ray against bound of projected control points
            Float maxWidth = std.max(Globals.Lerp(uMin, common.width[0], common.width[1]),
                                     Globals.Lerp(uMax, common.width[0], common.width[1]));
            Bounds3f curveBounds = Globals.Union(new Bounds3f(cp[0], cp[1]), new Bounds3f(cp[2], cp[3]));
            curveBounds = Globals.Expand(curveBounds, 0.5f * maxWidth);
            Bounds3f rayBounds = new Bounds3f(new Point3f(0, 0, 0), new Point3f(0, 0, Globals.Length(ray.d) * tMax));
            if (!Globals.Overlaps(rayBounds, curveBounds))
                return false;

            // Compute refinement depth for curve, _maxDepth_
            Float L0 = 0;
            for (int i = 0; i < 2; ++i)
                L0 = std.max(L0, std.max(std.max(std.abs(cp[i].x - 2 * cp[i + 1].x + cp[i + 2].x),
                                                 std.abs(cp[i].y - 2 * cp[i + 1].y + cp[i + 2].y)),
                                         std.abs(cp[i].z - 2 * cp[i + 1].z + cp[i + 2].z)));

            int maxDepth = 0;
            if (L0 > 0)
            {
                // Compute log base 4 by dividing log2 in half.
                Float eps = std.max(common.width[0], common.width[1]) * .05f;  // width / 20
                int r0 = Globals.Log2Int(1.41421356237f * 6.0f * L0 / (8.0f * eps)) / 2;
                maxDepth = Globals.Clamp(r0, 0, 10);
            }

            // Recursively test for ray--curve intersection
            pstd.span<Point3f> cpSpan = new pstd.span<Point3f>(cp);
            return RecursiveIntersect(ray, tMax, cpSpan, Globals.Inverse(rayFromObject), uMin, uMax, maxDepth, si);
        }

        bool RecursiveIntersect(Ray ray, Float tMax, pstd.span<Point3f> cp, Transform objectFromRay, Float u0,
                                Float u1, int depth, pstd.optional<ShapeIntersection>* si)
        {
            Float rayLength = Globals.Length(ray.d);
            if (depth > 0)
            {
                // Split curve segment into sub-segments and test for intersection
                pstd.array7<Point3f> cpSplit = Globals.SubdivideCubicBezier(cp);
                Float[] u = { u0, (u0 + u1) / 2, u1 };
                for (int seg = 0; seg < 2; ++seg)
                {
                    // Check ray against bounds of control points for segment
                    Float maxWidth = std.max(Globals.Lerp(u[seg], common.width[0], common.width[1]),
                                             Globals.Lerp(u[seg + 1], common.width[0], common.width[1]));
                    Point3f[] cpsArr = new Point3f[4] { cpSplit[3 * seg], cpSplit[3 * seg + 1], cpSplit[3 * seg + 2],
                                                        cpSplit[3 * seg + 3] };
                    pstd.span<Point3f> cps = new pstd.span<Point3f>(cpsArr);
                    Bounds3f curveBounds = Globals.Union(new Bounds3f(cps[0], cps[1]), new Bounds3f(cps[2], cps[3]));
                    curveBounds = Globals.Expand(curveBounds, 0.5f * maxWidth);
                    Bounds3f rayBounds = new Bounds3f(new Point3f(0, 0, 0),
                                                      new Point3f(0, 0, Globals.Length(ray.d) * tMax));
                    if (!Globals.Overlaps(rayBounds, curveBounds))
                        continue;

                    // Recursively test for ray--curve intersection
                    bool hit = RecursiveIntersect(ray, tMax, cps, objectFromRay, u[seg], u[seg + 1], depth - 1, si);
                    // If we found an intersection and this is a shadow ray, return
                    // true immediately.
                    if (hit && si == null)
                        return true;
                }
                return si != null ? (bool)(*si) : false;
            }
            else
            {
                // Intersect ray with curve segment
                // Test ray against segment endpoint boundaries
                // Test sample point against tangent perpendicular at curve start
                Float edge = (cp[1].y - cp[0].y) * -cp[0].y + cp[0].x * (cp[0].x - cp[1].x);
                if (edge < 0)
                    return false;

                // Test sample point against tangent perpendicular at curve end
                edge = (cp[2].y - cp[3].y) * -cp[3].y + cp[3].x * (cp[3].x - cp[2].x);
                if (edge < 0)
                    return false;

                // Find line $w$ that gives minimum distance to sample point
                Vector2f segmentDir = new Point2f(cp[3].x, cp[3].y) - new Point2f(cp[0].x, cp[0].y);
                Float denom = Globals.LengthSquared(segmentDir);
                if (denom == 0)
                    return false;
                Float w = Globals.Dot(-new Vector2f(cp[0].x, cp[0].y), segmentDir) / denom;

                // Compute $u$ coordinate of curve intersection point and _hitWidth_
                Float u = Globals.Clamp(Globals.Lerp(w, u0, u1), u0, u1);
                Float hitWidth = Globals.Lerp(u, common.width[0], common.width[1]);
                Normal3f nHit = default;
                if (common.type == CurveType.Ribbon)
                {
                    // Scale _hitWidth_ based on ribbon orientation
                    if (common.normalAngle == 0)
                        nHit = common.n[0];
                    else
                    {
                        Float sin0 = std.sin((1 - u) * common.normalAngle) * common.invSinNormalAngle;
                        Float sin1 = std.sin(u * common.normalAngle) * common.invSinNormalAngle;
                        nHit = sin0 * common.n[0] + sin1 * common.n[1];
                    }
                    hitWidth *= Globals.AbsDot(nHit, ray.d) / rayLength;
                }

                // Test intersection point against curve width
                Vector3f dpcdw;
                Point3f pc = Globals.EvaluateCubicBezier(cp, Globals.Clamp(w, 0, 1), &dpcdw);
                Float ptCurveDist2 = Globals.Sqr(pc.x) + Globals.Sqr(pc.y);
                if (ptCurveDist2 > Globals.Sqr(hitWidth) * 0.25f)
                    return false;
                if (pc.z < 0 || pc.z > rayLength * tMax)
                    return false;

                if (si != null)
                {
                    // Initialize _ShapeIntersection_ for curve intersection
                    // Compute _tHit_ for curve intersection
                    Float tHit = pc.z / rayLength;

                    // Try to reject an intersection that doesn't have a closer t than the existing one
                    if ((bool)(*si) && tHit > si->value().tHit)
                        return false;

                    // Initialize _SurfaceInteraction_ _intr_ for curve intersection
                    // Compute $v$ coordinate of curve intersection point
                    Float ptCurveDist = std.sqrt(ptCurveDist2);
                    Float edgeFunc = dpcdw.x * -pc.y + pc.x * dpcdw.y;
                    Float v = (edgeFunc > 0) ? 0.5f + ptCurveDist / hitWidth : 0.5f - ptCurveDist / hitWidth;

                    // Compute hit position and partial derivatives for curve intersection
                    Vector3f dpdu, dpdv;
                    Globals.EvaluateCubicBezier(new pstd.span<Point3f>(common.cpObj), u, &dpdu);
                    Globals.CHECK_NE(new Vector3f(0, 0, 0), dpdu);
                    if (common.type == CurveType.Ribbon)
                        dpdv = Globals.Normalize(Globals.Cross(nHit, dpdu)) * hitWidth;
                    else
                    {
                        // Compute curve $\dpdv$ for flat and cylinder curves
                        Vector3f dpduPlane = objectFromRay.ApplyInverse(dpdu);
                        Vector3f dpdvPlane = Globals.Normalize(new Vector3f(-dpduPlane.y, dpduPlane.x, 0)) * hitWidth;
                        if (common.type == CurveType.Cylinder)
                        {
                            // Rotate _dpdvPlane_ to give cylindrical appearance
                            Float theta = Globals.Lerp(v, -90, 90);
                            Transform rot = Globals.Rotate(-theta, dpduPlane);
                            dpdvPlane = rot.Apply(dpdvPlane);
                        }
                        dpdv = objectFromRay.Apply(dpdvPlane);
                    }

                    // Compute error bounds for curve intersection
                    Vector3f pError = new Vector3f(hitWidth, hitWidth, hitWidth);

                    bool flipNormal = common.reverseOrientation ^ common.transformSwapsHandedness;
                    Point3fi pi = new Point3fi(ray.Apply(tHit), pError);
                    SurfaceInteraction intr = new SurfaceInteraction(pi, new Point2f(u, v), -ray.d, dpdu, dpdv,
                                                                     new Normal3f(), new Normal3f(), ray.time,
                                                                     flipNormal);
                    intr = common.renderFromObject.Apply(intr);
                    *si = new ShapeIntersection(intr, tHit);
                }
                ++nCurveHits;
                return true;
            }
        }

        // Curve Private Members
        CurveCommon common;
        Float uMin, uMax;
    }

    // BilinearIntersection Definition
    public unsafe struct BilinearIntersection
    {
        public BilinearIntersection(Point2f uv, Float t) { this.uv = uv; this.t = t; }
        public override string ToString()
        {
            return Globals.StringPrintf("[ BilinearIntersection uv: %s t: %f", uv, t);
        }

        public Point2f uv;
        public Float t;
    }

    // BilinearPatch Definition
    public sealed unsafe class BilinearPatch : Shape
    {
        // STAT_PIXEL_RATIO("Intersections/Ray-bilinear patch intersection tests", nBLPHits, nBLPTests);
        // STAT_MEMORY_COUNTER("Memory/Bilinear patches", blpBytes);
        [ThreadStatic] static long nBLPHits, nBLPTests, blpBytes;
        static StatRegisterer STATS_REG = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportRatio("Intersections/Ray-bilinear patch intersection tests", nBLPHits, nBLPTests);
            nBLPHits = nBLPTests = 0;
            accum.ReportMemoryCounter("Memory/Bilinear patches", blpBytes);
            blpBytes = 0;
        });

        static List<BilinearPatchMesh> allMeshes;
        static readonly object allMeshesLock = new object();
        const Float MinSphericalSampleArea = 1e-4f;

        // BilinearPatch Public Methods
        public BilinearPatch(BilinearPatchMesh mesh, int meshIndex, int blpIndex)
        {
            this.meshIndex = meshIndex;
            this.blpIndex = blpIndex;
            blpBytes += 24;  // sizeof(*this) in pbrt-v4
            // Store area of bilinear patch in _area_
            // Get bilinear patch vertices in _p00_, _p01_, _p10_, and _p11_
            Point3f p00 = mesh.p[mesh.vertexIndices[4 * blpIndex]], p10 = mesh.p[mesh.vertexIndices[4 * blpIndex + 1]];
            Point3f p01 = mesh.p[mesh.vertexIndices[4 * blpIndex + 2]], p11 = mesh.p[mesh.vertexIndices[4 * blpIndex + 3]];

            if (IsRectangle(mesh))
                area = Globals.Distance(p00, p01) * Globals.Distance(p00, p10);
            else
            {
                // Compute approximate area of bilinear patch
                // Compute vertex grid for patch area approximation
                const int na = 3;
                Point3f[,] p = new Point3f[na + 1, na + 1];
                for (int i = 0; i <= na; ++i)
                {
                    Float u = (Float)i / (Float)na;
                    for (int j = 0; j <= na; ++j)
                    {
                        Float v = (Float)j / (Float)na;
                        p[i, j] = Globals.Lerp(u, Globals.Lerp(v, p00, p01), Globals.Lerp(v, p10, p11));
                    }
                }

                area = 0;
                for (int i = 0; i < na; ++i)
                    for (int j = 0; j < na; ++j)
                        area += 0.5f * Globals.Length(Globals.Cross(p[i + 1, j + 1] - p[i, j], p[i + 1, j] - p[i, j + 1]));
            }
        }

        public static void Init(Allocator alloc)
        {
            allMeshes = new List<BilinearPatchMesh>();
        }

        public static BilinearPatchMesh CreateMesh(Transform renderFromObject, bool reverseOrientation,
                                                   ParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            List<int> vertexIndices = parameters.GetIntArray("indices");
            List<Point3f> P = parameters.GetPoint3fArray("P");
            List<Point2f> uv = parameters.GetPoint2fArray("uv");

            if (vertexIndices.Count == 0)
            {
                if (P.Count == 4)
                    // single patch
                    vertexIndices = new List<int> { 0, 1, 2, 3 };
                else
                {
                    Globals.Error(loc, "Vertex indices \"indices\" must be provided with " +
                                       "bilinear patch mesh shape.");
                    return null;
                }
            }
            else if ((vertexIndices.Count % 4) != 0)
            {
                Globals.Error(loc, Globals.StringPrintf("Number of vertex indices %d not a multiple of 4. Discarding %d " +
                                                        "excess.", vertexIndices.Count, vertexIndices.Count % 4));
                while ((vertexIndices.Count % 4) != 0)
                    vertexIndices.RemoveAt(vertexIndices.Count - 1);
            }

            if (P.Count == 0)
            {
                Globals.Error(loc, "Vertex positions \"P\" must be provided with bilinear " +
                                   "patch mesh shape.");
                return null;
            }

            if (uv.Count != 0 && uv.Count != P.Count)
            {
                Globals.Error(loc, "Number of \"uv\"s for bilinear patch mesh must match \"P\"s. " +
                                   "Discarding uvs.");
                uv = new List<Point2f>();
            }

            List<Normal3f> N = parameters.GetNormal3fArray("N");
            if (N.Count != 0 && N.Count != P.Count)
            {
                Globals.Error(loc, "Number of \"N\"s for bilinear patch mesh must match \"P\"s. " +
                                   "Discarding \"N\"s.");
                N = new List<Normal3f>();
            }

            for (int i = 0; i < vertexIndices.Count; ++i)
                if (vertexIndices[i] >= P.Count)
                {
                    Globals.Error(loc, Globals.StringPrintf("Bilinear patch mesh has out of-bounds vertex index %d (%d " +
                                                            "\"P\" values were given. Discarding this mesh.",
                                                            vertexIndices[i], P.Count));
                    return null;
                }

            List<int> faceIndices = parameters.GetIntArray("faceIndices");
            if (faceIndices.Count != 0 && faceIndices.Count != vertexIndices.Count / 4)
            {
                Globals.Error(loc, Globals.StringPrintf("Number of face indices %d does not match number of bilinear " +
                                                        "patches %d. Discarding face indices.",
                                                        faceIndices.Count, vertexIndices.Count / 4));
                faceIndices = new List<int>();
            }

            // Bilinear patch emission image
            string filename = Globals.ResolveFilename(parameters.GetOneString("emissionfilename", ""));
            PiecewiseConstant2D imageDist = null;
            if (filename.Length != 0)
            {
                if (uv.Count != 0)
                    Globals.Error(loc, "\"emissionfilename\" is currently ignored for bilinear patches " +
                                       "if \"uv\" coordinates have been provided--sorry!");
                else
                {
                    ImageAndMetadata im = Image.Read(filename, alloc);

                    // Account for the fact that bilinear patch (u,v) maps (0,0) to the lower left
                    im.image.FlipY();

                    Bounds2f domain = new Bounds2f(new Point2f(0, 0), new Point2f(1, 1));
                    Array2D<Float> d = im.image.GetSamplingDistribution();
                    imageDist = new PiecewiseConstant2D(d, domain, alloc);
                }
            }

            return new BilinearPatchMesh(renderFromObject, reverseOrientation, new pstd.vector<int>(vertexIndices),
                                         new pstd.vector<Point3f>(P), new pstd.vector<Normal3f>(N),
                                         new pstd.vector<Point2f>(uv), new pstd.vector<int>(faceIndices), imageDist,
                                         alloc);
        }

        public static pstd.vector<Shape> CreatePatches(BilinearPatchMesh mesh, Allocator alloc)
        {
            int meshIndex;
            lock (allMeshesLock)
            {
                Globals.CHECK_LT(allMeshes.Count, 1 << 30);
                meshIndex = allMeshes.Count;
                allMeshes.Add(mesh);
            }

            pstd.vector<Shape> blps = new pstd.vector<Shape>((ulong)mesh.nPatches);
            for (int i = 0; i < mesh.nPatches; ++i)
                blps[i] = new BilinearPatch(mesh, meshIndex, i);

            return blps;
        }

        public override Bounds3f Bounds()
        {
            // Get bilinear patch vertices in _p00_, _p01_, _p10_, and _p11_
            BilinearPatchMesh mesh = GetMesh();
            int vi = 4 * blpIndex;
            Point3f p00 = mesh.p[mesh.vertexIndices[vi]], p10 = mesh.p[mesh.vertexIndices[vi + 1]];
            Point3f p01 = mesh.p[mesh.vertexIndices[vi + 2]], p11 = mesh.p[mesh.vertexIndices[vi + 3]];
            return Globals.Union(new Bounds3f(p00, p01), new Bounds3f(p10, p11));
        }

        public override DirectionCone NormalBounds()
        {
            // Get bilinear patch vertices in _p00_, _p01_, _p10_, and _p11_
            BilinearPatchMesh mesh = GetMesh();
            int vi = 4 * blpIndex;
            int[] v = { mesh.vertexIndices[vi], mesh.vertexIndices[vi + 1], mesh.vertexIndices[vi + 2], mesh.vertexIndices[vi + 3] };
            Point3f p00 = mesh.p[v[0]], p10 = mesh.p[v[1]];
            Point3f p01 = mesh.p[v[2]], p11 = mesh.p[v[3]];

            // If patch is a triangle, return bounds for single surface normal
            if (p00 == p10 || p10 == p11 || p11 == p01 || p01 == p00)
            {
                Vector3f dpdu = Globals.Lerp(0.5f, p10, p11) - Globals.Lerp(0.5f, p00, p01);
                Vector3f dpdv = Globals.Lerp(0.5f, p01, p11) - Globals.Lerp(0.5f, p00, p10);
                Vector3f nTri = Globals.Normalize(Globals.Cross(dpdu, dpdv));
                if (mesh.n != null)
                {
                    Normal3f ns = (mesh.n[v[0]] + mesh.n[v[1]] + mesh.n[v[2]] + mesh.n[v[3]]) / 4;
                    nTri = Globals.FaceForward(nTri, ns);
                }
                else if (mesh.reverseOrientation ^ mesh.transformSwapsHandedness)
                    nTri = -nTri;

                return new DirectionCone(nTri);
            }

            // Compute bilinear patch normal _n00_ at $(0,0)$
            Vector3f n00 = Globals.Normalize(Globals.Cross(p10 - p00, p01 - p00));
            if (mesh.n != null)
                n00 = Globals.FaceForward(n00, mesh.n[v[0]]);
            else if (mesh.reverseOrientation ^ mesh.transformSwapsHandedness)
                n00 = -n00;

            // Compute bilinear patch normals _n10_, _n01_, and _n11_
            Vector3f n10 = Globals.Normalize(Globals.Cross(p11 - p10, p00 - p10));
            Vector3f n01 = Globals.Normalize(Globals.Cross(p00 - p01, p11 - p01));
            Vector3f n11 = Globals.Normalize(Globals.Cross(p01 - p11, p10 - p11));
            if (mesh.n != null)
            {
                n10 = Globals.FaceForward(n10, mesh.n[v[1]]);
                n01 = Globals.FaceForward(n01, mesh.n[v[2]]);
                n11 = Globals.FaceForward(n11, mesh.n[v[3]]);
            }
            else if (mesh.reverseOrientation ^ mesh.transformSwapsHandedness)
            {
                n10 = -n10;
                n01 = -n01;
                n11 = -n11;
            }

            // Compute average normal and return normal bounds for patch
            Vector3f n = Globals.Normalize(n00 + n10 + n01 + n11);
            Float cosTheta = std.min(std.min(Globals.Dot(n, n00), Globals.Dot(n, n01)),
                                     std.min(Globals.Dot(n, n10), Globals.Dot(n, n11)));
            return new DirectionCone(n, Globals.Clamp(cosTheta, -1, 1));
        }

        public override pstd.optional<ShapeIntersection> Intersect(Ray ray, Float tMax = Infinity)
        {
            BilinearPatchMesh mesh = GetMesh();
            int vi = 4 * blpIndex;
            Point3f p00 = mesh.p[mesh.vertexIndices[vi]], p10 = mesh.p[mesh.vertexIndices[vi + 1]];
            Point3f p01 = mesh.p[mesh.vertexIndices[vi + 2]], p11 = mesh.p[mesh.vertexIndices[vi + 3]];
            pstd.optional<BilinearIntersection> blpIsect = Globals.IntersectBilinearPatch(ray, tMax, p00, p10, p01, p11);
            if (!blpIsect)
                return default;
            SurfaceInteraction intr = InteractionFromIntersection(mesh, blpIndex, blpIsect.value().uv, ray.time, -ray.d);
            return new ShapeIntersection(intr, blpIsect.value().t);
        }

        public override bool IntersectP(Ray ray, Float tMax = Infinity)
        {
            BilinearPatchMesh mesh = GetMesh();
            int vi = 4 * blpIndex;
            Point3f p00 = mesh.p[mesh.vertexIndices[vi]], p10 = mesh.p[mesh.vertexIndices[vi + 1]];
            Point3f p01 = mesh.p[mesh.vertexIndices[vi + 2]], p11 = mesh.p[mesh.vertexIndices[vi + 3]];
            return Globals.IntersectBilinearPatch(ray, tMax, p00, p10, p01, p11);
        }

        public override Float Area() { return area; }

        public static SurfaceInteraction InteractionFromIntersection(BilinearPatchMesh mesh, int blpIndex, Point2f uv,
                                                                     Float time, Vector3f wo)
        {
            // Compute bilinear patch point $\pt{}$, $\dpdu$, and $\dpdv$ for $(u,v)$
            // Get bilinear patch vertices in _p00_, _p01_, _p10_, and _p11_
            int vi = 4 * blpIndex;
            int[] v = { mesh.vertexIndices[vi], mesh.vertexIndices[vi + 1], mesh.vertexIndices[vi + 2], mesh.vertexIndices[vi + 3] };
            Point3f p00 = mesh.p[v[0]], p10 = mesh.p[v[1]];
            Point3f p01 = mesh.p[v[2]], p11 = mesh.p[v[3]];

            Point3f p = Globals.Lerp(uv[0], Globals.Lerp(uv[1], p00, p01), Globals.Lerp(uv[1], p10, p11));
            Vector3f dpdu = Globals.Lerp(uv[1], p10, p11) - Globals.Lerp(uv[1], p00, p01);
            Vector3f dpdv = Globals.Lerp(uv[0], p01, p11) - Globals.Lerp(uv[0], p00, p10);

            // Compute $(s,t)$ texture coordinates at bilinear patch $(u,v)$
            Point2f st = uv;
            Float duds = 1, dudt = 0, dvds = 0, dvdt = 1;
            if (mesh.uv != null)
            {
                // Compute texture coordinates for bilinear patch intersection point
                Point2f uv00 = mesh.uv[v[0]], uv10 = mesh.uv[v[1]];
                Point2f uv01 = mesh.uv[v[2]], uv11 = mesh.uv[v[3]];
                st = Globals.Lerp(uv[0], Globals.Lerp(uv[1], uv00, uv01), Globals.Lerp(uv[1], uv10, uv11));

                // Update bilinear patch $\dpdu$ and $\dpdv$ accounting for $(s,t)$
                // Compute partial derivatives of $(u,v)$ with respect to $(s,t)$
                Vector2f dstdu = Globals.Lerp(uv[1], uv10, uv11) - Globals.Lerp(uv[1], uv00, uv01);
                Vector2f dstdv = Globals.Lerp(uv[0], uv01, uv11) - Globals.Lerp(uv[0], uv00, uv10);

                duds = std.abs(dstdu[0]) < 1e-8f ? 0 : 1 / dstdu[0];
                dvds = std.abs(dstdv[0]) < 1e-8f ? 0 : 1 / dstdv[0];
                dudt = std.abs(dstdu[1]) < 1e-8f ? 0 : 1 / dstdu[1];
                dvdt = std.abs(dstdv[1]) < 1e-8f ? 0 : 1 / dstdv[1];

                // Compute partial derivatives of $\pt{}$ with respect to $(s,t)$
                Vector3f dpds = dpdu * duds + dpdv * dvds;
                Vector3f dpdt = dpdu * dudt + dpdv * dvdt;

                // Set _dpdu_ and _dpdv_ to updated partial derivatives
                if (Globals.Cross(dpds, dpdt) != new Vector3f(0, 0, 0))
                {
                    if (Globals.Dot(Globals.Cross(dpdu, dpdv), Globals.Cross(dpds, dpdt)) < 0)
                        dpdt = -dpdt;

                    Globals.DCHECK_GE(Globals.Dot(Globals.Normalize(Globals.Cross(dpdu, dpdv)),
                                                  Globals.Normalize(Globals.Cross(dpds, dpdt))), -1e-3f);
                    dpdu = dpds;
                    dpdv = dpdt;
                }
            }

            // Find partial derivatives $\dndu$ and $\dndv$ for bilinear patch
            Vector3f d2Pduu = new Vector3f(0, 0, 0), d2Pdvv = new Vector3f(0, 0, 0);
            Vector3f d2Pduv = (p00 - p01) + (p11 - p10);
            // Compute coefficients for fundamental forms
            Float E = Globals.Dot(dpdu, dpdu), F = Globals.Dot(dpdu, dpdv), G = Globals.Dot(dpdv, dpdv);
            Vector3f n = Globals.Normalize(Globals.Cross(dpdu, dpdv));
            Float e = Globals.Dot(n, d2Pduu), f = Globals.Dot(n, d2Pduv), g = Globals.Dot(n, d2Pdvv);

            // Compute $\dndu$ and $\dndv$ from fundamental form coefficients
            Float EGF2 = Globals.DifferenceOfProducts(E, G, F, F);
            Float invEGF2 = (EGF2 == 0) ? (Float)0 : 1 / EGF2;
            Normal3f dndu = (Normal3f)((f * F - e * G) * invEGF2 * dpdu + (e * F - f * E) * invEGF2 * dpdv);
            Normal3f dndv = (Normal3f)((g * F - f * G) * invEGF2 * dpdu + (f * F - g * E) * invEGF2 * dpdv);

            // Update $\dndu$ and $\dndv$ to account for $(s,t)$ parameterization
            Normal3f dnds = dndu * duds + dndv * dvds;
            Normal3f dndt = dndu * dudt + dndv * dvdt;
            dndu = dnds;
            dndv = dndt;

            // Initialize bilinear patch intersection point error _pError_
            Point3f pAbsSum = Globals.Abs(p00) + Globals.Abs(p01) + Globals.Abs(p10) + Globals.Abs(p11);
            Vector3f pError = Globals.gamma(6) * (Vector3f)pAbsSum;

            // Initialize _SurfaceInteraction_ for bilinear patch intersection
            int faceIndex = mesh.faceIndices != null ? mesh.faceIndices[blpIndex] : 0;
            bool flipNormal = mesh.reverseOrientation ^ mesh.transformSwapsHandedness;
            SurfaceInteraction isect = new SurfaceInteraction(new Point3fi(p, pError), st, wo, dpdu, dpdv, dndu, dndv,
                                                              time, flipNormal, faceIndex);

            // Compute bilinear patch shading normal if necessary
            if (mesh.n != null)
            {
                // Compute shading normals for bilinear patch intersection point
                Normal3f n00 = mesh.n[v[0]], n10 = mesh.n[v[1]];
                Normal3f n01 = mesh.n[v[2]], n11 = mesh.n[v[3]];
                Normal3f ns = Globals.Lerp(uv[0], Globals.Lerp(uv[1], n00, n01), Globals.Lerp(uv[1], n10, n11));
                if (Globals.LengthSquared(ns) > 0)
                {
                    ns = Globals.Normalize(ns);
                    // Set shading geometry for bilinear patch intersection
                    Normal3f dnduS = Globals.Lerp(uv[1], n10, n11) - Globals.Lerp(uv[1], n00, n01);
                    Normal3f dndvS = Globals.Lerp(uv[0], n01, n11) - Globals.Lerp(uv[0], n00, n10);
                    // Update $\dndu$ and $\dndv$ to account for $(s,t)$ parameterization
                    Normal3f dndsS = dnduS * duds + dndvS * dvds;
                    Normal3f dndtS = dnduS * dudt + dndvS * dvdt;
                    dnduS = dndsS;
                    dndvS = dndtS;

                    Transform r = Globals.RotateFromTo((Vector3f)Globals.Normalize(isect.n), (Vector3f)ns);
                    isect.SetShadingGeometry(ns, r.Apply(dpdu), r.Apply(dpdv), dnduS, dndvS, true);
                }
            }

            return isect;
        }

        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            BilinearPatchMesh mesh = GetMesh();
            int vi = 4 * blpIndex;
            int[] v = { mesh.vertexIndices[vi], mesh.vertexIndices[vi + 1], mesh.vertexIndices[vi + 2], mesh.vertexIndices[vi + 3] };
            Point3f p00 = mesh.p[v[0]], p10 = mesh.p[v[1]];
            Point3f p01 = mesh.p[v[2]], p11 = mesh.p[v[3]];

            // Sample bilinear patch parametric $(u,v)$ coordinates
            Float pdf = 1;
            Point2f uv;
            if (mesh.imageDistribution != null)
                // Sample patch according to image distribution
                uv = mesh.imageDistribution.Sample(u, &pdf);
            else if (!IsRectangle(mesh))
            {
                // Sample patch $(u,v)$ with approximate uniform area sampling
                // Initialize _w_ array with differential area at bilinear patch corners
                pstd.span<Float> w = new pstd.span<Float>(new Float[] {
                    Globals.Length(Globals.Cross(p10 - p00, p01 - p00)), Globals.Length(Globals.Cross(p10 - p00, p11 - p10)),
                    Globals.Length(Globals.Cross(p01 - p00, p11 - p01)), Globals.Length(Globals.Cross(p11 - p10, p11 - p01)) });

                uv = Globals.SampleBilinear(u, w);
                pdf = Globals.BilinearPDF(uv, w);
            }
            else
                uv = u;

            // Compute bilinear patch geometric quantities at sampled $(u,v)$
            // Compute $\pt{}$, $\dpdu$, and $\dpdv$ for sampled $(u,v)$
            Point3f pu0 = Globals.Lerp(uv[1], p00, p01), pu1 = Globals.Lerp(uv[1], p10, p11);
            Point3f p = Globals.Lerp(uv[0], pu0, pu1);
            Vector3f dpdu = pu1 - pu0;
            Vector3f dpdv = Globals.Lerp(uv[0], p01, p11) - Globals.Lerp(uv[0], p00, p10);
            if (Globals.LengthSquared(dpdu) == 0 || Globals.LengthSquared(dpdv) == 0)
                return default;

            // Compute $(s,t)$ coordinates at sampled $(u,v)$
            Point2f st = uv;
            if (mesh.uv != null)
            {
                // Compute texture coordinates for bilinear patch intersection point
                Point2f uv00 = mesh.uv[v[0]], uv10 = mesh.uv[v[1]];
                Point2f uv01 = mesh.uv[v[2]], uv11 = mesh.uv[v[3]];
                st = Globals.Lerp(uv[0], Globals.Lerp(uv[1], uv00, uv01), Globals.Lerp(uv[1], uv10, uv11));
            }

            // Compute surface normal for sampled bilinear patch $(u,v)$
            Normal3f n = (Normal3f)Globals.Normalize(Globals.Cross(dpdu, dpdv));
            // Flip normal if necessary
            if (mesh.n != null)
            {
                // Compute shading normals for bilinear patch intersection point
                Normal3f n00 = mesh.n[v[0]], n10 = mesh.n[v[1]];
                Normal3f n01 = mesh.n[v[2]], n11 = mesh.n[v[3]];
                Normal3f ns = Globals.Lerp(uv[0], Globals.Lerp(uv[1], n00, n01), Globals.Lerp(uv[1], n10, n11));
                n = Globals.FaceForward(n, ns);
            }
            else if (mesh.reverseOrientation ^ mesh.transformSwapsHandedness)
                n = -n;

            // Compute _pError_ for sampled bilinear patch $(u,v)$
            Point3f pAbsSum = Globals.Abs(p00) + Globals.Abs(p01) + Globals.Abs(p10) + Globals.Abs(p11);
            Vector3f pError = Globals.gamma(6) * (Vector3f)pAbsSum;

            // Return _ShapeSample_ for sampled bilinear patch point
            return new ShapeSample(new Interaction(new Point3fi(p, pError), n, st),
                                   pdf / Globals.Length(Globals.Cross(dpdu, dpdv)));
        }

        public override Float PDF(Interaction intr)
        {
            // Get bilinear patch vertices in _p00_, _p01_, _p10_, and _p11_
            BilinearPatchMesh mesh = GetMesh();
            int vi = 4 * blpIndex;
            int[] v = { mesh.vertexIndices[vi], mesh.vertexIndices[vi + 1], mesh.vertexIndices[vi + 2], mesh.vertexIndices[vi + 3] };
            Point3f p00 = mesh.p[v[0]], p10 = mesh.p[v[1]];
            Point3f p01 = mesh.p[v[2]], p11 = mesh.p[v[3]];

            // Compute parametric $(u,v)$ of point on bilinear patch
            Point2f uv = intr.uv;
            if (mesh.uv != null)
            {
                // Compute texture coordinates for bilinear patch intersection point
                Point2f uv00 = mesh.uv[v[0]], uv10 = mesh.uv[v[1]];
                Point2f uv01 = mesh.uv[v[2]], uv11 = mesh.uv[v[3]];
                uv = Globals.InvertBilinear(uv, new pstd.span<Point2f>(new Point2f[] { uv00, uv10, uv01, uv11 }));
            }

            // Compute PDF for sampling the $(u,v)$ coordinates given by _intr.uv_
            Float pdf;
            if (mesh.imageDistribution != null)
                // Return PDF for sampling $(u,v)$ from image distribution
                pdf = mesh.imageDistribution.PDF(uv);
            else if (!IsRectangle(mesh))
            {
                // Return PDF for bilinear area sampling
                pstd.span<Float> w = new pstd.span<Float>(new Float[] {
                    Globals.Length(Globals.Cross(p10 - p00, p01 - p00)), Globals.Length(Globals.Cross(p10 - p00, p11 - p10)),
                    Globals.Length(Globals.Cross(p01 - p00, p11 - p01)), Globals.Length(Globals.Cross(p11 - p10, p11 - p01)) });
                pdf = Globals.BilinearPDF(uv, w);
            }
            else
                pdf = 1;

            // Find $\dpdu$ and $\dpdv$ at bilinear patch $(u,v)$
            // Compute $\pt{}$, $\dpdu$, and $\dpdv$ for sampled $(u,v)$
            Point3f pu0 = Globals.Lerp(uv[1], p00, p01), pu1 = Globals.Lerp(uv[1], p10, p11);
            Vector3f dpdu = pu1 - pu0;
            Vector3f dpdv = Globals.Lerp(uv[0], p01, p11) - Globals.Lerp(uv[0], p00, p10);

            // Return final bilinear patch area sampling PDF
            return pdf / Globals.Length(Globals.Cross(dpdu, dpdv));
        }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            // Get bilinear patch vertices in _p00_, _p01_, _p10_, and _p11_
            BilinearPatchMesh mesh = GetMesh();
            int vi = 4 * blpIndex;
            int[] v = { mesh.vertexIndices[vi], mesh.vertexIndices[vi + 1], mesh.vertexIndices[vi + 2], mesh.vertexIndices[vi + 3] };
            Point3f p00 = mesh.p[v[0]], p10 = mesh.p[v[1]];
            Point3f p01 = mesh.p[v[2]], p11 = mesh.p[v[3]];

            // Sample bilinear patch using uniform area sampling
            // Compute solid angle of bilinear patch and test against threshold
            Vector3f v00 = Globals.Normalize(p00 - ctx.p()), v10 = Globals.Normalize(p10 - ctx.p());
            Vector3f v01 = Globals.Normalize(p01 - ctx.p()), v11 = Globals.Normalize(p11 - ctx.p());
            if (!IsRectangle(mesh) || mesh.imageDistribution != null ||
                Globals.SphericalQuadArea(v00, v10, v11, v01) <= MinSphericalSampleArea)
                return ShapeUtil.SampleByArea(this, ctx, u);

            // Sample spherical rectangle at reference point
            // Warp sample _u_ to account for incident $\cos\theta$ factor
            Float pdf = 1;
            if (ctx.ns != new Normal3f(0, 0, 0))
            {
                // Compute $\cos\theta$ weights for rectangle seen from reference point
                pstd.span<Float> w = new pstd.span<Float>(new Float[] {
                    std.max<Float>(0.01f, Globals.AbsDot(v00, ctx.ns)), std.max<Float>(0.01f, Globals.AbsDot(v10, ctx.ns)),
                    std.max<Float>(0.01f, Globals.AbsDot(v01, ctx.ns)), std.max<Float>(0.01f, Globals.AbsDot(v11, ctx.ns)) });
                u = Globals.SampleBilinear(u, w);
                pdf *= Globals.BilinearPDF(u, w);
            }

            Vector3f eu = p10 - p00, ev = p01 - p00;
            Float quadPDF;
            Point3f p = Globals.SampleSphericalRectangle(ctx.p(), p00, eu, ev, u, &quadPDF);
            pdf *= quadPDF;

            // Compute $(u,v)$ and surface normal for sampled point on rectangle
            Point2f uv = new Point2f(Globals.Dot(p - p00, eu) / Globals.DistanceSquared(p10, p00),
                                     Globals.Dot(p - p00, ev) / Globals.DistanceSquared(p01, p00));
            Normal3f n = (Normal3f)Globals.Normalize(Globals.Cross(eu, ev));
            // Flip normal if necessary
            if (mesh.n != null)
            {
                // Compute shading normals for bilinear patch intersection point
                Normal3f n00 = mesh.n[v[0]], n10 = mesh.n[v[1]];
                Normal3f n01 = mesh.n[v[2]], n11 = mesh.n[v[3]];
                Normal3f ns = Globals.Lerp(uv[0], Globals.Lerp(uv[1], n00, n01), Globals.Lerp(uv[1], n10, n11));
                n = Globals.FaceForward(n, ns);
            }
            else if (mesh.reverseOrientation ^ mesh.transformSwapsHandedness)
                n = -n;

            // Compute $(s,t)$ texture coordinates for sampled $(u,v)$
            Point2f st = uv;
            if (mesh.uv != null)
            {
                // Compute texture coordinates for bilinear patch intersection point
                Point2f uv00 = mesh.uv[v[0]], uv10 = mesh.uv[v[1]];
                Point2f uv01 = mesh.uv[v[2]], uv11 = mesh.uv[v[3]];
                st = Globals.Lerp(uv[0], Globals.Lerp(uv[1], uv00, uv01), Globals.Lerp(uv[1], uv10, uv11));
            }

            return new ShapeSample(new Interaction(p, n, ctx.time, st), pdf);
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            // Get bilinear patch vertices in _p00_, _p01_, _p10_, and _p11_
            BilinearPatchMesh mesh = GetMesh();
            int vi = 4 * blpIndex;
            int[] v = { mesh.vertexIndices[vi], mesh.vertexIndices[vi + 1], mesh.vertexIndices[vi + 2], mesh.vertexIndices[vi + 3] };
            Point3f p00 = mesh.p[v[0]], p10 = mesh.p[v[1]];
            Point3f p01 = mesh.p[v[2]], p11 = mesh.p[v[3]];

            // Compute solid angle PDF for sampling bilinear patch from _ctx_
            // Intersect sample ray with shape geometry
            Ray ray = ctx.SpawnRay(wi);
            pstd.optional<ShapeIntersection> isect = Intersect(ray);
            if (!isect)
                return 0;

            // Compute PDF for sampling the intersection point
            // Test for rectangle and compute solid angle
            Vector3f v00 = Globals.Normalize(p00 - ctx.p()), v10 = Globals.Normalize(p10 - ctx.p());
            Vector3f v01 = Globals.Normalize(p01 - ctx.p()), v11 = Globals.Normalize(p11 - ctx.p());
            if (!IsRectangle(mesh) || mesh.imageDistribution != null ||
                Globals.SphericalQuadArea(v00, v10, v11, v01) <= MinSphericalSampleArea)
            {
                // Return solid angle PDF for area-sampled bilinear patch
                Float pdf = PDF(isect.value().intr) * (Globals.DistanceSquared(ctx.p(), isect.value().intr.p()) /
                                                       Globals.AbsDot(isect.value().intr.n, -wi));
                return Globals.IsInf(pdf) ? 0 : pdf;
            }
            else
            {
                // Return PDF for sampled bilinear patch
                Float pdf = 1 / Globals.SphericalQuadArea(v00, v10, v11, v01);
                if (ctx.ns != new Normal3f(0, 0, 0))
                {
                    // Compute $\cos\theta$ weights for rectangle seen from reference point
                    pstd.span<Float> w = new pstd.span<Float>(new Float[] {
                        std.max<Float>(0.01f, Globals.AbsDot(v00, ctx.ns)), std.max<Float>(0.01f, Globals.AbsDot(v10, ctx.ns)),
                        std.max<Float>(0.01f, Globals.AbsDot(v01, ctx.ns)), std.max<Float>(0.01f, Globals.AbsDot(v11, ctx.ns)) });
                    Point2f u = Globals.InvertSphericalRectangleSample(ctx.p(), p00, p10 - p00, p01 - p00,
                                                                       isect.value().intr.p());
                    return Globals.BilinearPDF(u, w) * pdf;
                }
                else
                    return pdf;
            }
        }

        public override string ToString()
        {
            return Globals.StringPrintf("[ BilinearPatch meshIndex: %d blpIndex: %d area: %f ]", meshIndex, blpIndex,
                                        area);
        }

        // BilinearPatch Private Methods
        BilinearPatchMesh GetMesh() { return allMeshes[meshIndex]; }

        bool IsRectangle(BilinearPatchMesh mesh)
        {
            // Get bilinear patch vertices in _p00_, _p01_, _p10_, and _p11_
            int vi = 4 * blpIndex;
            Point3f p00 = mesh.p[mesh.vertexIndices[vi]], p10 = mesh.p[mesh.vertexIndices[vi + 1]];
            Point3f p01 = mesh.p[mesh.vertexIndices[vi + 2]], p11 = mesh.p[mesh.vertexIndices[vi + 3]];

            if (p00 == p01 || p01 == p11 || p11 == p10 || p10 == p00)
                return false;
            // Check if bilinear patch vertices are coplanar
            Normal3f n = (Normal3f)Globals.Normalize(Globals.Cross(p10 - p00, p01 - p00));
            if (Globals.AbsDot((Vector3f)Globals.Normalize(p11 - p00), n) > 1e-5f)
                return false;

            // Check if planar vertices form a rectangle
            Point3f pCenter = (p00 + (Vector3f)p01 + (Vector3f)p10 + (Vector3f)p11) / 4;
            Float[] d2 = { Globals.DistanceSquared(p00, pCenter), Globals.DistanceSquared(p01, pCenter),
                           Globals.DistanceSquared(p10, pCenter), Globals.DistanceSquared(p11, pCenter) };
            for (int i = 1; i < 4; ++i)
                if (std.abs(d2[i] - d2[0]) / d2[0] > 1e-4f)
                    return false;
            return true;
        }

        // BilinearPatch Private Members
        int meshIndex, blpIndex;
        Float area;
    }

    public static unsafe partial class Globals
    {
        // Triangle Function Declarations
        public static pstd.optional<TriangleIntersection> IntersectTriangle(Ray ray, Float tMax, Point3f p0, Point3f p1,
                                                                             Point3f p2)
        {
            // Return no intersection if triangle is degenerate
            if (LengthSquared(Cross(p2 - p0, p1 - p0)) == 0)
                return default;

            // Transform triangle vertices to ray coordinate space
            // Translate vertices based on ray origin
            Point3f p0t = p0 - (Vector3f)ray.o;
            Point3f p1t = p1 - (Vector3f)ray.o;
            Point3f p2t = p2 - (Vector3f)ray.o;

            // Permute components of triangle vertices and ray direction
            int kz = MaxComponentIndex(Abs(ray.d));
            int kx = kz + 1;
            if (kx == 3)
                kx = 0;
            int ky = kx + 1;
            if (ky == 3)
                ky = 0;
            Vector3f d = Permute(ray.d, kx, ky, kz);
            p0t = Permute(p0t, kx, ky, kz);
            p1t = Permute(p1t, kx, ky, kz);
            p2t = Permute(p2t, kx, ky, kz);

            // Apply shear transformation to translated vertex positions
            Float Sx = -d.x / d.z;
            Float Sy = -d.y / d.z;
            Float Sz = 1 / d.z;
            p0t.x += Sx * p0t.z;
            p0t.y += Sy * p0t.z;
            p1t.x += Sx * p1t.z;
            p1t.y += Sy * p1t.z;
            p2t.x += Sx * p2t.z;
            p2t.y += Sy * p2t.z;

            // Compute edge function coefficients _e0_, _e1_, and _e2_
            Float e0 = DifferenceOfProducts(p1t.x, p2t.y, p1t.y, p2t.x);
            Float e1 = DifferenceOfProducts(p2t.x, p0t.y, p2t.y, p0t.x);
            Float e2 = DifferenceOfProducts(p0t.x, p1t.y, p0t.y, p1t.x);

            // Fall back to double-precision test at triangle edges
            if (sizeof(Float) == sizeof(float) && (e0 == 0.0f || e1 == 0.0f || e2 == 0.0f))
            {
                double p2txp1ty = (double)p2t.x * (double)p1t.y;
                double p2typ1tx = (double)p2t.y * (double)p1t.x;
                e0 = (float)(p2typ1tx - p2txp1ty);
                double p0txp2ty = (double)p0t.x * (double)p2t.y;
                double p0typ2tx = (double)p0t.y * (double)p2t.x;
                e1 = (float)(p0typ2tx - p0txp2ty);
                double p1txp0ty = (double)p1t.x * (double)p0t.y;
                double p1typ0tx = (double)p1t.y * (double)p0t.x;
                e2 = (float)(p1typ0tx - p1txp0ty);
            }

            // Perform triangle edge and determinant tests
            if ((e0 < 0 || e1 < 0 || e2 < 0) && (e0 > 0 || e1 > 0 || e2 > 0))
                return default;
            Float det = e0 + e1 + e2;
            if (det == 0)
                return default;

            // Compute scaled hit distance to triangle and test against ray $t$ range
            p0t.z *= Sz;
            p1t.z *= Sz;
            p2t.z *= Sz;
            Float tScaled = e0 * p0t.z + e1 * p1t.z + e2 * p2t.z;
            if (det < 0 && (tScaled >= 0 || tScaled < tMax * det))
                return default;
            else if (det > 0 && (tScaled <= 0 || tScaled > tMax * det))
                return default;

            // Compute barycentric coordinates and $t$ value for triangle intersection
            Float invDet = 1 / det;
            Float b0 = e0 * invDet, b1 = e1 * invDet, b2 = e2 * invDet;
            Float t = tScaled * invDet;
            DCHECK(!IsNaN(t));

            // Ensure that computed triangle $t$ is conservatively greater than zero
            // Compute $\delta_z$ term for triangle $t$ error bounds
            Float maxZt = MaxComponentValue(Abs(new Vector3f(p0t.z, p1t.z, p2t.z)));
            Float deltaZ = gamma(3) * maxZt;

            // Compute $\delta_x$ and $\delta_y$ terms for triangle $t$ error bounds
            Float maxXt = MaxComponentValue(Abs(new Vector3f(p0t.x, p1t.x, p2t.x)));
            Float maxYt = MaxComponentValue(Abs(new Vector3f(p0t.y, p1t.y, p2t.y)));
            Float deltaX = gamma(5) * (maxXt + maxZt);
            Float deltaY = gamma(5) * (maxYt + maxZt);

            // Compute $\delta_e$ term for triangle $t$ error bounds
            Float deltaE = 2 * (gamma(2) * maxXt * maxYt + deltaY * maxXt + deltaX * maxYt);

            // Compute $\delta_t$ term for triangle $t$ error bounds and check _t_
            Float maxE = MaxComponentValue(Abs(new Vector3f(e0, e1, e2)));
            Float deltaT = 3 * (gamma(3) * maxE * maxZt + deltaE * maxZt + deltaZ * maxE) * std.abs(invDet);
            if (t <= deltaT)
                return default;

            return new TriangleIntersection(b0, b1, b2, t);
        }

        // Bilinear Patch Inline Functions
        public static pstd.optional<BilinearIntersection> IntersectBilinearPatch(Ray ray, Float tMax, Point3f p00,
                                                                                 Point3f p10, Point3f p01, Point3f p11)
        {
            // Find quadratic coefficients for distance from ray to $u$ iso-lines
            Float a = Dot(Cross(p10 - p00, p01 - p11), ray.d);
            Float c = Dot(Cross(p00 - ray.o, ray.d), p01 - p00);
            Float b = Dot(Cross(p10 - ray.o, ray.d), p11 - p10) - (a + c);

            // Solve quadratic for bilinear patch $u$ intersection
            Float u1, u2;
            if (!Quadratic(a, b, c, &u1, &u2))
                return default;

            // Find epsilon _eps_ to ensure that candidate $t$ is greater than zero
            Float eps = gamma(30) * (MaxComponentValue(Abs(ray.o)) + MaxComponentValue(Abs(ray.d)) +
                                     MaxComponentValue(Abs(p00)) + MaxComponentValue(Abs(p10)) +
                                     MaxComponentValue(Abs(p01)) + MaxComponentValue(Abs(p11)));

            // Compute $v$ and $t$ for the first $u$ intersection
            Float t = tMax, u = 0, v = 0;
            if (0 <= u1 && u1 <= 1)
            {
                // Precompute common terms for $v$ and $t$ computation
                Point3f uo = Lerp(u1, p00, p10);
                Vector3f ud = Lerp(u1, p01, p11) - uo;
                Vector3f deltao = uo - ray.o;
                Vector3f perp = Cross(ray.d, ud);
                Float p2 = LengthSquared(perp);

                // Compute matrix determinants for $v$ and $t$ numerators
                Float v1 = Determinant(new SquareMatrix3(deltao.x, ray.d.x, perp.x, deltao.y, ray.d.y, perp.y,
                                                         deltao.z, ray.d.z, perp.z));
                Float t1 = Determinant(new SquareMatrix3(deltao.x, ud.x, perp.x, deltao.y, ud.y, perp.y, deltao.z,
                                                         ud.z, perp.z));

                // Set _u_, _v_, and _t_ if intersection is valid
                if (t1 > p2 * eps && 0 <= v1 && v1 <= p2)
                {
                    u = u1;
                    v = v1 / p2;
                    t = t1 / p2;
                }
            }

            // Compute $v$ and $t$ for the second $u$ intersection
            if (0 <= u2 && u2 <= 1 && u2 != u1)
            {
                // Precompute common terms for $v$ and $t$ computation
                Point3f uo = Lerp(u2, p00, p10);
                Vector3f ud = Lerp(u2, p01, p11) - uo;
                Vector3f deltao = uo - ray.o;
                Vector3f perp = Cross(ray.d, ud);
                Float p2 = LengthSquared(perp);

                // Compute matrix determinants for $v$ and $t$ numerators
                Float v2 = Determinant(new SquareMatrix3(deltao.x, ray.d.x, perp.x, deltao.y, ray.d.y, perp.y,
                                                         deltao.z, ray.d.z, perp.z));
                Float t2 = Determinant(new SquareMatrix3(deltao.x, ud.x, perp.x, deltao.y, ud.y, perp.y, deltao.z,
                                                         ud.z, perp.z));
                t2 /= p2;

                // Set _u_, _v_, and _t_ if intersection is valid
                if (0 <= v2 && v2 <= p2 && t > t2 && t2 > eps)
                {
                    t = t2;
                    u = u2;
                    v = v2 / p2;
                }
            }

            // TODO: reject hits with sufficiently small t that we're not sure.
            // Check intersection $t$ against _tMax_ and possibly return intersection
            if (t >= tMax)
                return default;

            return new BilinearIntersection(new Point2f(u, v), t);
        }
    }

    // Shape factory (Shape::Create from shapes.cpp)
    public abstract unsafe partial class Shape
    {
        // STAT_COUNTER("Geometry/Spheres", nSpheres); etc.
        [ThreadStatic] static long nSpheres, nCylinders, nDisks, displacedTrisDelta;
        static StatRegisterer STATS_REG_SHAPE = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportCounter("Geometry/Spheres", nSpheres);
            nSpheres = 0;
            accum.ReportCounter("Geometry/Cylinders", nCylinders);
            nCylinders = 0;
            accum.ReportCounter("Geometry/Disks", nDisks);
            nDisks = 0;
            accum.ReportCounter("Geometry/Triangles added from displacement mapping", displacedTrisDelta);
            displacedTrisDelta = 0;
        });

        public static pstd.vector<Shape> Create(string name, Transform renderFromObject, Transform objectFromRender,
                                                bool reverseOrientation, ParameterDictionary parameters,
                                                IDictionary<string, FloatTexture> floatTextures, FileLoc loc,
                                                Allocator alloc)
        {
            pstd.vector<Shape> shapes = new pstd.vector<Shape>();
            if (name == "sphere")
            {
                shapes.push_back(Sphere.Create(renderFromObject, objectFromRender, reverseOrientation, parameters, loc, alloc));
                ++nSpheres;
            }
            // Create remaining quadrics
            else if (name == "cylinder")
            {
                shapes.push_back(Cylinder.Create(renderFromObject, objectFromRender, reverseOrientation, parameters, loc, alloc));
                ++nCylinders;
            }
            else if (name == "disk")
            {
                shapes.push_back(Disk.Create(renderFromObject, objectFromRender, reverseOrientation, parameters, loc, alloc));
                ++nDisks;
            }
            else if (name == "bilinearmesh")
            {
                BilinearPatchMesh mesh = BilinearPatch.CreateMesh(renderFromObject, reverseOrientation, parameters, loc, alloc);
                if (mesh != null)
                    shapes = BilinearPatch.CreatePatches(mesh, alloc);
            }
            // Create multiple-_Shape_ types
            else if (name == "curve")
                shapes = Curve.Create(renderFromObject, objectFromRender, reverseOrientation, parameters, loc, alloc);
            else if (name == "trianglemesh")
            {
                TriangleMesh mesh = Triangle.CreateMesh(renderFromObject, reverseOrientation, parameters, loc, alloc);
                if (mesh != null)
                    shapes = Triangle.CreateTriangles(mesh, alloc);
            }
            else if (name == "plymesh")
            {
                string filename = Globals.ResolveFilename(parameters.GetOneString("filename", ""));
                TriQuadMesh plyMesh = TriQuadMesh.ReadPLY(filename);

                Float edgeLength = parameters.GetOneFloat("edgelength", 1.0f);
                edgeLength *= Globals.Options.displacementEdgeScale;

                string displacementTexName = parameters.GetTexture("displacement");
                if (displacementTexName.Length != 0)
                {
                    if (!floatTextures.TryGetValue(displacementTexName, out FloatTexture displacement))
                        Globals.ErrorExit(loc, Globals.StringPrintf("%s: no such texture defined.", displacementTexName));

                    Globals.LOG_VERBOSE("Starting to displace mesh \"%s\" with \"%s\"", filename, displacementTexName);

                    int origTriCount = (int)(plyMesh.triIndices.size() / 3);
                    Transform rfo = renderFromObject;
                    plyMesh = plyMesh.Displace(
                        (Point3f v0, Point3f v1) =>
                        {
                            v0 = rfo.Apply(v0);
                            v1 = rfo.Apply(v1);
                            return Globals.Distance(v0, v1);
                        },
                        edgeLength,
                        (Point3f[] p, Normal3f[] n, Point2f[] uv, int nVertices) =>
                        {
                            Globals.ParallelFor(0, nVertices, (int i) =>
                            {
                                TextureEvalContext ctx = default;
                                ctx.p = p[i];
                                ctx.uv = uv[i];
                                Float d = new UniversalTextureEvaluator().Apply(displacement, ctx);
                                p[i] += (Vector3f)(d * n[i]);
                            });
                        },
                        loc);

                    Globals.LOG_VERBOSE("Finished displacing mesh \"%s\" with \"%s\" -> %d tris", filename,
                                        displacementTexName, plyMesh.triIndices.size() / 3);
                    displacedTrisDelta += (long)(plyMesh.triIndices.size() / 3) - origTriCount;
                }

                if (!plyMesh.triIndices.empty())
                {
                    TriangleMesh mesh = new TriangleMesh(renderFromObject, reverseOrientation, plyMesh.triIndices,
                                                         plyMesh.p, new pstd.vector<Vector3f>(), plyMesh.n,
                                                         plyMesh.uv, plyMesh.faceIndices, alloc);
                    shapes = Triangle.CreateTriangles(mesh, alloc);
                }

                if (!plyMesh.quadIndices.empty())
                {
                    BilinearPatchMesh mesh = new BilinearPatchMesh(renderFromObject, reverseOrientation,
                                                                   plyMesh.quadIndices, plyMesh.p, plyMesh.n,
                                                                   plyMesh.uv, plyMesh.faceIndices,
                                                                   null /* image dist */, alloc);
                    pstd.vector<Shape> quadMesh = BilinearPatch.CreatePatches(mesh, alloc);
                    for (int i = 0; i < (int)quadMesh.size(); ++i)
                        shapes.push_back(quadMesh[i]);
                }
            }
            else if (name == "loopsubdiv")
            {
                int nLevels = parameters.GetOneInt("levels", 3);
                List<int> vertexIndices = parameters.GetIntArray("indices");
                if (vertexIndices.Count == 0)
                    Globals.ErrorExit(loc, "Vertex indices \"indices\" not provided for " +
                                           "LoopSubdiv shape.");
                List<Point3f> P = parameters.GetPoint3fArray("P");
                if (P.Count == 0)
                    Globals.ErrorExit(loc, "Vertex positions \"P\" not provided for LoopSubdiv shape.");

                string scheme = parameters.GetOneString("scheme", "loop");

                Transform rfo = renderFromObject;
                TriangleMesh mesh = Globals.LoopSubdivide(&rfo, reverseOrientation, nLevels,
                                                          new pstd.span<int>(vertexIndices.ToArray()),
                                                          new pstd.span<Point3f>(P.ToArray()), alloc);
                shapes = Triangle.CreateTriangles(mesh, alloc);
            }
            else
                Globals.ErrorExit(loc, Globals.StringPrintf("%s: shape type unknown.", name));

            if (shapes.empty())
                Globals.ErrorExit(loc, Globals.StringPrintf("%s: unable to create shape.", name));
            return shapes;
        }
    }

}  // namespace pbrt
