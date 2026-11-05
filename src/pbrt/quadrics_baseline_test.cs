// (not in pbrt-v4) quadrics_baseline_test.cs
//
// The sphere, disk and cylinder of shapes.cs as they were before the QuadricBase refactor (commit 02b50cf), kept to benchmark and compare accuracy of the current ones.

using System;
using System.Collections.Generic;

namespace pbrt
{
    // Shared helpers for the shape implementations below
    // BaselineSphere Definition
    public sealed unsafe class BaselineSphere : Shape
    {
        // BaselineSphere Public Methods
        public BaselineSphere(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation, Float radius,
                      Float zMin, Float zMax, Float phiMax)
        {
            this.renderFromObject = renderFromObject;
            this.objectFromRender = objectFromRender;
            this.reverseOrientation = reverseOrientation;
            this.transformSwapsHandedness = renderFromObject.SwapsHandedness();
            this.radius = radius;
            this.zMin = pbrt.Clamp(std.min(zMin, zMax), -radius, radius);
            this.zMax = pbrt.Clamp(std.max(zMin, zMax), -radius, radius);
            this.thetaZMin = std.acos(pbrt.Clamp(std.min(zMin, zMax) / radius, -1, 1));
            this.thetaZMax = std.acos(pbrt.Clamp(std.max(zMin, zMax) / radius, -1, 1));
            this.phiMax = pbrt.Radians(pbrt.Clamp(phiMax, 0, 360));
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
            Interval a = pbrt.Sqr(di.x) + pbrt.Sqr(di.y) + pbrt.Sqr(di.z);
            Interval b = 2 * (di.x * oi.x + di.y * oi.y + di.z * oi.z);
            Interval c = pbrt.Sqr(oi.x) + pbrt.Sqr(oi.y) + pbrt.Sqr(oi.z) - pbrt.Sqr(new Interval(radius));

            // Compute sphere quadratic discriminant _discrim_
            Interval f = b / (2 * a);
            Vector3fi v = new Vector3fi(oi.x - f * di.x, oi.y - f * di.y, oi.z - f * di.z);
            Interval length = pbrt.Length(v);
            Interval discrim = 4 * a * (new Interval(radius) + length) * (new Interval(radius) - length);
            if (discrim.LowerBound() < 0)
                return default;

            // Compute quadratic $t$ values
            Interval rootDiscrim = pbrt.Sqrt(discrim);
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
            pHit = (Point3f)oi + (Float)tShapeHit * (Vector3f)di;
            // Refine sphere intersection point
            pHit *= radius / pbrt.Distance(pHit, new Point3f(0, 0, 0));

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
                pHit = (Point3f)oi + (Float)tShapeHit * (Vector3f)di;
                // Refine sphere intersection point
                pHit *= radius / pbrt.Distance(pHit, new Point3f(0, 0, 0));

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
            Float theta = pbrt.SafeACos(cosTheta);
            Float v = (theta - thetaZMin) / (thetaZMax - thetaZMin);
            // Compute sphere $\dpdu$ and $\dpdv$
            Float zRadius = std.sqrt(pbrt.Sqr(pHit.x) + pbrt.Sqr(pHit.y));
            Float cosPhi = pHit.x / zRadius, sinPhi = pHit.y / zRadius;
            Vector3f dpdu = new Vector3f(-phiMax * pHit.y, phiMax * pHit.x, 0);
            Float sinTheta = pbrt.SafeSqrt(1 - pbrt.Sqr(cosTheta));
            Vector3f dpdv = (thetaZMax - thetaZMin) *
                            new Vector3f(pHit.z * cosPhi, pHit.z * sinPhi, -radius * sinTheta);

            // Compute sphere $\dndu$ and $\dndv$
            Vector3f d2Pduu = -phiMax * phiMax * new Vector3f(pHit.x, pHit.y, 0);
            Vector3f d2Pduv = (thetaZMax - thetaZMin) * pHit.z * phiMax * new Vector3f(-sinPhi, cosPhi, 0.0f);
            Vector3f d2Pdvv = -pbrt.Sqr(thetaZMax - thetaZMin) * new Vector3f(pHit.x, pHit.y, pHit.z);
            // Compute coefficients for fundamental forms
            Float E = pbrt.Dot(dpdu, dpdu), F = pbrt.Dot(dpdu, dpdv), G = pbrt.Dot(dpdv, dpdv);
            Vector3f n = pbrt.Normalize(pbrt.Cross(dpdu, dpdv));
            Float e = pbrt.Dot(n, d2Pduu), f = pbrt.Dot(n, d2Pduv), g = pbrt.Dot(n, d2Pdvv);

            // Compute $\dndu$ and $\dndv$ from fundamental form coefficients
            Float EGF2 = pbrt.DifferenceOfProducts(E, G, F, F);
            Float invEGF2 = (EGF2 == 0) ? (Float)0 : 1 / EGF2;
            Normal3f dndu = (Normal3f)((f * F - e * G) * invEGF2 * dpdu + (e * F - f * E) * invEGF2 * dpdv);
            Normal3f dndv = (Normal3f)((g * F - f * G) * invEGF2 * dpdu + (f * F - g * E) * invEGF2 * dpdv);

            // Compute error bounds for sphere intersection
            Vector3f pError = pbrt.gamma(5) * pbrt.Abs((Vector3f)pHit);

            // Return _SurfaceInteraction_ for quadric intersection
            bool flipNormal = reverseOrientation ^ transformSwapsHandedness;
            Vector3f woObject = objectFromRender.Apply(wo);
            return renderFromObject.Apply(new SurfaceInteraction(new Point3fi(pHit, pError), new Point2f(u, v),
                                                                 woObject, dpdu, dpdv, dndu, dndv, time, flipNormal));
        }

        public override Float Area() { return phiMax * radius * (zMax - zMin); }

        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            Point3f pObj = new Point3f(0, 0, 0) + radius * pbrt.SampleUniformSphere(u);
            // Reproject _pObj_ to sphere surface and compute _pObjError_
            pObj *= radius / pbrt.Distance(pObj, new Point3f(0, 0, 0));
            Vector3f pObjError = pbrt.gamma(5) * pbrt.Abs((Vector3f)pObj);
            // Compute surface normal for sphere sample and return _ShapeSample_
            Normal3f nObj = new Normal3f(pObj.x, pObj.y, pObj.z);
            Normal3f n = pbrt.Normalize(renderFromObject.Apply(nObj));
            if (reverseOrientation)
                n *= -1;

            // Compute $(u,v)$ coordinates for sphere sample
            Float theta = pbrt.SafeACos(pObj.z / radius);
            Float phi = std.atan2(pObj.y, pObj.x);
            if (phi < 0)
                phi += 2 * Pi;
            Point2f uv = new Point2f(phi / phiMax, (theta - thetaZMin) / (thetaZMax - thetaZMin));

            Point3fi pi = renderFromObject.Apply(new Point3fi(pObj, pObjError));
            return new ShapeSample(new Interaction(pi, n, uv), 1 / Area());
        }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            // Sample uniformly on sphere if $\pt{}$ is inside it
            Point3f pCenter = renderFromObject.Apply(new Point3f(0, 0, 0));
            Point3f pOrigin = ctx.OffsetRayOrigin(pCenter);
            if (pbrt.DistanceSquared(pOrigin, pCenter) <= pbrt.Sqr(radius))
            {
                // Sample shape by area and compute incident direction _wi_
                pstd.optional<ShapeSample> ss = Sample(u);
                pbrt.DCHECK(ss);
                ss.Ref().intr.time = ctx.time;
                Vector3f wi = ss.value().intr.p() - ctx.p();
                if (pbrt.LengthSquared(wi) == 0)
                    return default;
                wi = pbrt.Normalize(wi);

                // Convert area sampling PDF in _ss_ to solid angle measure
                ss.Ref().pdf /= pbrt.AbsDot(ss.value().intr.n, -wi) / pbrt.DistanceSquared(ctx.p(), ss.value().intr.p());
                if (pbrt.IsInf(ss.value().pdf))
                    return default;

                return ss;
            }

            // Sample sphere uniformly inside subtended cone
            // Compute quantities related to the $\theta_\roman{max}$ for cone
            Float sinThetaMax = radius / pbrt.Distance(ctx.p(), pCenter);
            Float sin2ThetaMax = pbrt.Sqr(sinThetaMax);
            Float cosThetaMax = pbrt.SafeSqrt(1 - sin2ThetaMax);
            Float oneMinusCosThetaMax = 1 - cosThetaMax;

            // Compute $\theta$ and $\phi$ values for sample in cone
            Float cosTheta = (cosThetaMax - 1) * u[0] + 1;
            Float sin2Theta = 1 - pbrt.Sqr(cosTheta);
            if (sin2ThetaMax < 0.00068523f /* sin^2(1.5 deg) */)
            {
                // Compute cone sample via Taylor series expansion for small angles
                sin2Theta = sin2ThetaMax * u[0];
                cosTheta = std.sqrt(1 - sin2Theta);
                oneMinusCosThetaMax = sin2ThetaMax / 2;
            }

            // Compute angle $\alpha$ from center of sphere to sampled point on surface
            Float cosAlpha = sin2Theta / sinThetaMax +
                             cosTheta * pbrt.SafeSqrt(1 - sin2Theta / pbrt.Sqr(sinThetaMax));
            Float sinAlpha = pbrt.SafeSqrt(1 - pbrt.Sqr(cosAlpha));

            // Compute surface normal and sampled point on sphere
            Float phi = u[1] * 2 * Pi;
            Vector3f w = pbrt.SphericalDirection(sinAlpha, cosAlpha, phi);
            Frame samplingFrame = Frame.FromZ(pbrt.Normalize(pCenter - ctx.p()));
            Normal3f n = (Normal3f)samplingFrame.FromLocal(-w);
            Point3f p = pCenter + radius * new Point3f(n.x, n.y, n.z);
            if (reverseOrientation)
                n *= -1;

            // Return _ShapeSample_ for sampled point on sphere
            // Compute _pError_ for sampled point on sphere
            Vector3f pError = pbrt.gamma(5) * pbrt.Abs((Vector3f)p);

            // Compute $(u,v)$ coordinates for sampled point on sphere
            Point3f pObj = objectFromRender.Apply(p);
            Float theta = pbrt.SafeACos(pObj.z / radius);
            Float spherePhi = std.atan2(pObj.y, pObj.x);
            if (spherePhi < 0)
                spherePhi += 2 * Pi;
            Point2f uv = new Point2f(spherePhi / phiMax, (theta - thetaZMin) / (thetaZMax - thetaZMin));

            pbrt.DCHECK_NE(oneMinusCosThetaMax, 0);  // very small far away sphere
            return new ShapeSample(new Interaction(new Point3fi(p, pError), n, ctx.time, uv),
                                   1 / (2 * Pi * oneMinusCosThetaMax));
        }

        public override Bounds3f Bounds()
        {
            return renderFromObject.Apply(
                new Bounds3f(new Point3f(-radius, -radius, zMin), new Point3f(radius, radius, zMax)));
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ BaselineSphere renderFromObject: %s " +
                                        "objectFromRender: %s reverseOrientation: %s " +
                                        "transformSwapsHandedness: %s radius: %f zMin: %f " +
                                        "zMax: %f thetaMin: %f " +
                                        "thetaMax: %f phiMax: %f ]",
                                        renderFromObject, objectFromRender, reverseOrientation,
                                        transformSwapsHandedness, radius, zMin, zMax, thetaZMin,
                                        thetaZMax, phiMax);
        }

        public override Float PDF(Interaction intr) { return 1 / Area(); }

        public static BaselineSphere Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation,
                                    ParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            Float radius = parameters.GetOneFloat("radius", 1.0f);
            Float zmin = parameters.GetOneFloat("zmin", -radius);
            Float zmax = parameters.GetOneFloat("zmax", radius);
            Float phimax = parameters.GetOneFloat("phimax", 360.0f);
            return new BaselineSphere(renderFromObject, objectFromRender, reverseOrientation, radius, zmin, zmax, phimax);
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            Point3f pCenter = renderFromObject.Apply(new Point3f(0, 0, 0));
            Point3f pOrigin = ctx.OffsetRayOrigin(pCenter);
            if (pbrt.DistanceSquared(pOrigin, pCenter) <= pbrt.Sqr(radius))
            {
                // Intersect sample ray with shape geometry
                Ray ray = ctx.SpawnRay(wi);
                pstd.optional<ShapeIntersection> isect = Intersect(ray);
                if (!isect)
                    return 0;

                // Compute PDF in solid angle measure from shape intersection point
                Float pdf = (1 / Area()) / (pbrt.AbsDot(isect.value().intr.n, -wi) /
                                            pbrt.DistanceSquared(ctx.p(), isect.value().intr.p()));
                if (pbrt.IsInf(pdf))
                    pdf = 0;

                return pdf;
            }
                // Return solid angle PDF for point inside sphere

            // Compute general solid angle sphere PDF
            Float sin2ThetaMax = radius * radius / pbrt.DistanceSquared(ctx.p(), pCenter);
            Float cosThetaMax = pbrt.SafeSqrt(1 - sin2ThetaMax);
            Float oneMinusCosThetaMax = 1 - cosThetaMax;
            // Compute more accurate _oneMinusCosThetaMax_ for small solid angle
            if (sin2ThetaMax < 0.00068523f /* sin^2(1.5 deg) */)
                oneMinusCosThetaMax = sin2ThetaMax / 2;

            return 1 / (2 * Pi * oneMinusCosThetaMax);
        }

        // BaselineSphere Private Members
        Float radius;
        Float zMin, zMax;
        Float thetaZMin, thetaZMax, phiMax;
        Transform renderFromObject, objectFromRender;
        bool reverseOrientation, transformSwapsHandedness;
    }

    // BaselineDisk Definition
    public sealed unsafe class BaselineDisk : Shape
    {
        // BaselineDisk Public Methods
        public BaselineDisk(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation, Float height,
                    Float radius, Float innerRadius, Float phiMax)
        {
            this.renderFromObject = renderFromObject;
            this.objectFromRender = objectFromRender;
            this.reverseOrientation = reverseOrientation;
            this.transformSwapsHandedness = renderFromObject.SwapsHandedness();
            this.height = height;
            this.radius = radius;
            this.innerRadius = innerRadius;
            this.phiMax = pbrt.Radians(pbrt.Clamp(phiMax, 0, 360));
        }

        public override Float Area() { return phiMax * 0.5f * (pbrt.Sqr(radius) - pbrt.Sqr(innerRadius)); }

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
            Point3f pHit = (Point3f)oi + (Float)tShapeHit * (Vector3f)di;
            Float dist2 = pbrt.Sqr(pHit.x) + pbrt.Sqr(pHit.y);
            if (dist2 > pbrt.Sqr(radius) || dist2 < pbrt.Sqr(innerRadius))
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
            Float rHit = std.sqrt(pbrt.Sqr(pHit.x) + pbrt.Sqr(pHit.y));
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
            Point2f pd = pbrt.SampleUniformDiskConcentric(u);
            Point3f pObj = new Point3f(pd.x * radius, pd.y * radius, height);
            Point3fi pi = renderFromObject.Apply(new Point3fi(pObj));
            Normal3f n = pbrt.Normalize(renderFromObject.Apply(new Normal3f(0, 0, 1)));
            if (reverseOrientation)
                n *= -1;
            // Compute $(u,v)$ for sampled point on disk
            Float phi = std.atan2(pd.y, pd.x);
            if (phi < 0)
                phi += 2 * Pi;
            Float radiusSample = std.sqrt(pbrt.Sqr(pObj.x) + pbrt.Sqr(pObj.y));
            Point2f uv = new Point2f(phi / phiMax, (radius - radiusSample) / (radius - innerRadius));
            return new ShapeSample(new Interaction(pi, n, uv), 1 / Area());
        }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            // Sample shape by area and compute incident direction _wi_
            pstd.optional<ShapeSample> ss = Sample(u);
            pbrt.DCHECK(ss);
            ss.Ref().intr.time = ctx.time;
            Vector3f wi = ss.value().intr.p() - ctx.p();
            if (pbrt.LengthSquared(wi) == 0)
                return default;
            wi = pbrt.Normalize(wi);

            // Convert area sampling PDF in _ss_ to solid angle measure
            ss.Ref().pdf /= pbrt.AbsDot(ss.value().intr.n, -wi) / pbrt.DistanceSquared(ctx.p(), ss.value().intr.p());
            if (pbrt.IsInf(ss.value().pdf))
                return default;

            return ss;
        }

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

        public override string ToString()
        {
            return pbrt.StringPrintf(
                "[ BaselineDisk renderFromObject: %s objectFromRender: %s " +
                "reverseOrientation: %s " +
                "transformSwapsHandedness: %s height: %f radius: %f innerRadius: %f " +
                "phiMax: %f ]",
                renderFromObject, objectFromRender, reverseOrientation,
                transformSwapsHandedness, height, radius, innerRadius, phiMax);
        }

        public override Float PDF(Interaction intr) { return 1 / Area(); }

        public static BaselineDisk Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation,
                                  ParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            Float height = parameters.GetOneFloat("height", 0.0f);
            Float radius = parameters.GetOneFloat("radius", 1);
            Float innerRadius = parameters.GetOneFloat("innerradius", 0);
            Float phimax = parameters.GetOneFloat("phimax", 360);
            return new BaselineDisk(renderFromObject, objectFromRender, reverseOrientation, height, radius, innerRadius, phimax);
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            // Intersect sample ray with shape geometry
            Ray ray = ctx.SpawnRay(wi);
            pstd.optional<ShapeIntersection> isect = Intersect(ray);
            if (!isect)
                return 0;

            // Compute PDF in solid angle measure from shape intersection point
            Float pdf = (1 / Area()) / (pbrt.AbsDot(isect.value().intr.n, -wi) /
                                        pbrt.DistanceSquared(ctx.p(), isect.value().intr.p()));
            if (pbrt.IsInf(pdf))
                pdf = 0;

            return pdf;
        }

        // BaselineDisk Private Members
        Transform renderFromObject, objectFromRender;
        bool reverseOrientation, transformSwapsHandedness;
        Float height, radius, innerRadius, phiMax;
    }

    // BaselineCylinder Definition
    public sealed unsafe class BaselineCylinder : Shape
    {

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
            Interval a = pbrt.Sqr(di.x) + pbrt.Sqr(di.y);
            Interval b = 2 * (di.x * oi.x + di.y * oi.y);
            Interval c = pbrt.Sqr(oi.x) + pbrt.Sqr(oi.y) - pbrt.Sqr(new Interval(radius));

            // Compute cylinder quadratic discriminant _discrim_
            Interval f = b / (2 * a);
            Interval vx = oi.x - f * di.x, vy = oi.y - f * di.y;
            Interval length = pbrt.Sqrt(pbrt.Sqr(vx) + pbrt.Sqr(vy));
            Interval discrim = 4 * a * (new Interval(radius) + length) * (new Interval(radius) - length);
            if (discrim.LowerBound() < 0)
                return default;

            // Compute quadratic $t$ values
            Interval rootDiscrim = pbrt.Sqrt(discrim);
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
            pHit = (Point3f)oi + (Float)tShapeHit * (Vector3f)di;
            // Refine cylinder intersection point
            Float hitRad = std.sqrt(pbrt.Sqr(pHit.x) + pbrt.Sqr(pHit.y));
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
                pHit = (Point3f)oi + (Float)tShapeHit * (Vector3f)di;
                // Refine cylinder intersection point
                Float hitRad2 = std.sqrt(pbrt.Sqr(pHit.x) + pbrt.Sqr(pHit.y));
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
            Float E = pbrt.Dot(dpdu, dpdu), F = pbrt.Dot(dpdu, dpdv), G = pbrt.Dot(dpdv, dpdv);
            Vector3f n = pbrt.Normalize(pbrt.Cross(dpdu, dpdv));
            Float e = pbrt.Dot(n, d2Pduu), f = pbrt.Dot(n, d2Pduv), g = pbrt.Dot(n, d2Pdvv);

            // Compute $\dndu$ and $\dndv$ from fundamental form coefficients
            Float EGF2 = pbrt.DifferenceOfProducts(E, G, F, F);
            Float invEGF2 = (EGF2 == 0) ? (Float)0 : 1 / EGF2;
            Normal3f dndu = (Normal3f)((f * F - e * G) * invEGF2 * dpdu + (e * F - f * E) * invEGF2 * dpdv);
            Normal3f dndv = (Normal3f)((g * F - f * G) * invEGF2 * dpdu + (f * F - g * E) * invEGF2 * dpdv);

            // Compute error bounds for cylinder intersection
            Vector3f pError = pbrt.gamma(3) * pbrt.Abs(new Vector3f(pHit.x, pHit.y, 0));

            // Return _SurfaceInteraction_ for quadric intersection
            bool flipNormal = reverseOrientation ^ transformSwapsHandedness;
            Vector3f woObject = objectFromRender.Apply(wo);
            return renderFromObject.Apply(new SurfaceInteraction(new Point3fi(pHit, pError), new Point2f(u, v),
                                                                 woObject, dpdu, dpdv, dndu, dndv, time, flipNormal));
        }

        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            Float z = pbrt.Lerp(u[0], zMin, zMax);
            Float phi = u[1] * phiMax;
            // Compute cylinder sample position _pi_ and normal _n_ from $z$ and $\phi$
            Point3f pObj = new Point3f(radius * std.cos(phi), radius * std.sin(phi), z);
            // Reproject _pObj_ to cylinder surface and compute _pObjError_
            Float hitRad = std.sqrt(pbrt.Sqr(pObj.x) + pbrt.Sqr(pObj.y));
            pObj.x *= radius / hitRad;
            pObj.y *= radius / hitRad;
            Vector3f pObjError = pbrt.gamma(3) * pbrt.Abs(new Vector3f(pObj.x, pObj.y, 0));

            Point3fi pi = renderFromObject.Apply(new Point3fi(pObj, pObjError));
            Normal3f n = pbrt.Normalize(renderFromObject.Apply(new Normal3f(pObj.x, pObj.y, 0)));
            if (reverseOrientation)
                n *= -1;
            // Compute $(u,v)$ for cylinder sample
            Point2f uv = new Point2f(phi / phiMax, (pObj.z - zMin) / (zMax - zMin));
            return new ShapeSample(new Interaction(pi, n, uv), 1 / Area());
        }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            // Sample shape by area and compute incident direction _wi_
            pstd.optional<ShapeSample> ss = Sample(u);
            pbrt.DCHECK(ss);
            ss.Ref().intr.time = ctx.time;
            Vector3f wi = ss.value().intr.p() - ctx.p();
            if (pbrt.LengthSquared(wi) == 0)
                return default;
            wi = pbrt.Normalize(wi);

            // Convert area sampling PDF in _ss_ to solid angle measure
            ss.Ref().pdf /= pbrt.AbsDot(ss.value().intr.n, -wi) / pbrt.DistanceSquared(ctx.p(), ss.value().intr.p());
            if (pbrt.IsInf(ss.value().pdf))
                return default;

            return ss;
        }
        // BaselineCylinder Public Methods
        public BaselineCylinder(Transform renderFromObj, Transform objFromRender, bool reverseOrientation, Float radius,
                        Float zMin, Float zMax, Float phiMax)
        {
            this.renderFromObject = renderFromObj;
            this.objectFromRender = objFromRender;
            this.reverseOrientation = reverseOrientation;
            this.transformSwapsHandedness = renderFromObj.SwapsHandedness();
            this.radius = radius;
            this.zMin = std.min(zMin, zMax);
            this.zMax = std.max(zMin, zMax);
            this.phiMax = pbrt.Radians(pbrt.Clamp(phiMax, 0, 360));
        }

        public override Bounds3f Bounds()
        {
            return renderFromObject.Apply(
                new Bounds3f(new Point3f(-radius, -radius, zMin), new Point3f(radius, radius, zMax)));
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ BaselineCylinder renderFromObject: %s objectFromRender: %s " +
                                        "reverseOrientation: %s " +
                                        "transformSwapsHandedness: %s radius: %f zMin: %f zMax: %f " +
                                        "phiMax: %f ]",
                                        renderFromObject, objectFromRender, reverseOrientation,
                                        transformSwapsHandedness, radius, zMin, zMax, phiMax);
        }

        public override Float PDF(Interaction intr) { return 1 / Area(); }

        public static BaselineCylinder Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation,
                                      ParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            Float radius = parameters.GetOneFloat("radius", 1);
            Float zmin = parameters.GetOneFloat("zmin", -1);
            Float zmax = parameters.GetOneFloat("zmax", 1);
            Float phimax = parameters.GetOneFloat("phimax", 360);
            return new BaselineCylinder(renderFromObject, objectFromRender, reverseOrientation, radius, zmin, zmax, phimax);
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            // Intersect sample ray with shape geometry
            Ray ray = ctx.SpawnRay(wi);
            pstd.optional<ShapeIntersection> isect = Intersect(ray);
            if (!isect)
                return 0;

            // Compute PDF in solid angle measure from shape intersection point
            Float pdf = (1 / Area()) / (pbrt.AbsDot(isect.value().intr.n, -wi) /
                                        pbrt.DistanceSquared(ctx.p(), isect.value().intr.p()));
            if (pbrt.IsInf(pdf))
                pdf = 0;

            return pdf;
        }

        // BaselineCylinder Private Members
        Transform renderFromObject, objectFromRender;
        bool reverseOrientation, transformSwapsHandedness;
        Float radius, zMin, zMax, phiMax;
    }

}
