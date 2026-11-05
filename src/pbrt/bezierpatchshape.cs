// (not in pbrt-v4) bezierpatchshape.cs
//
// A bicubic patch that is intersected with Bezier clipping (bezierclip.cs). The patch is an intersection proxy: the Bezier patch bounds the hit,
// and the exact surface (the limit surface of a subdivision patch, a PN triangle, ...) is evaluated at the (u, v) of the hit for the position,
// the derivatives and the normal.

using System;
using System.Collections.Generic;

namespace pbrt
{
    // The surface of one patch, evaluated in object space at the patch parameters (u, v) in [0, 1]^2
    public abstract class PatchSurface
    {
        // The position, the partial derivatives (with respect to the texture coordinates uv), the texture coordinates, and a shading normal
        // (zero when the normal is the cross product of the derivatives)
        public abstract void Evaluate(double u, double v, out SdfVector p, out SdfVector dpdu, out SdfVector dpdv, out Point2f uv,
                                      out SdfVector shadingNormal);

        // The index of the face of the patch (ptex face id)
        public virtual int FaceIndex()
        {
            return 0;
        }
    }

    // The shading geometry of a patch: the tangent is made orthogonal to the shading normal as for the triangles of pbrt (the shading frame of a BSDF
    // must be orthonormal)
    public static unsafe class PatchShading
    {
        public static void Apply(SurfaceInteraction si, Normal3f ns, Vector3f dpdu)
        {
            Vector3f ss = dpdu;
            Vector3f ts = pbrt.Cross(ns, ss);
            if (pbrt.LengthSquared(ts) > 0)
            {
                ss = pbrt.Cross(ts, ns);
            }
            else
            {
                Vector3f a, c;
                pbrt.CoordinateSystem(ns, &a, &c);
                ss = a;
                ts = c;
            }
            si.SetShadingGeometry(ns, ss, ts, new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), true);
        }
    }

    // The parameters that the patches of a surface share
    public sealed class BezierSurfaceSettings
    {
        public Transform renderFromObject;
        public Transform objectFromRender;
        public bool reverseOrientation;
        public bool transformSwapsHandedness;
        // The size of the final bilinear patch (object space), when the angular size of a pixel is not given
        public double epsilon = 1e-5;
        // The angular size of a pixel in radians, 0 to use epsilon. The size of the bilinear patch is a quarter of the footprint of a pixel.
        public double pixelSpread = 0;
        public BezierClipOptions clip = BezierClipOptions.Default();
        // The maximum level of the clipping from the size of the patch and epsilon, instead of clip.maxLevel (when it is larger): a leaf of the size
        // of a patch of the default level is much larger than epsilon for a large patch, and a ray that grazes the surface finds the leaf and not the surface
        public bool autoMaxLevel = true;
        // The cells along each axis of the table that draws the points of the surface for area lights
        public int sampleResolution = 16;
    }

    public sealed unsafe class BezierClipPatchShape : Shape
    {
        // conservative: also test the four children of the patch where the ray fails near its border (for transition patches)
        public BezierClipPatchShape(BezierSurfaceSettings settings, in BezierPatch4 patch, PatchSurface surface, bool conservative)
        {
            this.settings = settings;
            this.surface = surface;
            this.patch = patch;
            intersection = new BezierPatchIntersection(patch);
            BezierClipOptions options = settings.clip;
            options.conservative = options.conservative || conservative;
            SdfVector low = intersection.BoundsMin, high = intersection.BoundsMax;
            if (settings.autoMaxLevel)
            {
                double size = (high - low).Length();
                int needed = size > settings.epsilon && settings.epsilon > 0 ? (int)Math.Ceiling(Math.Log(size / settings.epsilon, 2)) + 1 : 10;
                options.maxLevel = Math.Max(options.maxLevel, Math.Min(needed, 24));
            }
            intersection.Options = options;
            Bounds3f result = new Bounds3f();
            for (int corner = 0; corner < 8; ++corner)
            {
                Point3f point = settings.renderFromObject.Apply(new Point3f((Float)((corner & 1) != 0 ? high.x : low.x),
                                                                            (Float)((corner & 2) != 0 ? high.y : low.y),
                                                                            (Float)((corner & 4) != 0 ? high.z : low.z)));
                result = corner == 0 ? new Bounds3f(point) : pbrt.Union(result, point);
            }
            Vector3f pad = new Vector3f(1e-4f, 1e-4f, 1e-4f) * pbrt.Length(result.pMax - result.pMin);
            bounds = new Bounds3f(result.pMin - pad, result.pMax + pad);
            center = (low + high) * 0.5;
        }

        public override Bounds3f Bounds()
        {
            return bounds;
        }

        public override DirectionCone NormalBounds()
        {
            return DirectionCone.EntireSphere();
        }

        public override pstd.optional<ShapeIntersection> Intersect(Ray ray, Float tMax = Infinity)
        {
            if (!Trace(ray, tMax, out BezierClipHit hit, out double length))
            {
                return default;
            }
            SurfaceInteraction intr = InteractionFromHit(ray, hit);
            return new ShapeIntersection(intr, (Float)(hit.t / length));
        }

        public override bool IntersectP(Ray ray, Float tMax = Infinity)
        {
            return Trace(ray, tMax, out BezierClipHit hit, out double length);
        }

        public override Float Area()
        {
            return (Float)Sampler().Area;
        }

        // The sampler of the area of the patch (built when first needed)
        private PatchAreaSampler Sampler()
        {
            PatchAreaSampler result = sampler;
            if (result == null)
            {
                result = new PatchAreaSampler((double u, double v) =>
                {
                    surface.Evaluate(u, v, out SdfVector p, out SdfVector dpdu, out SdfVector dpdv, out Point2f uv, out SdfVector shading);
                    Point3f q = settings.renderFromObject.Apply(new Point3f((Float)p.x, (Float)p.y, (Float)p.z));
                    return new SdfVector(q.x, q.y, q.z);
                }, settings.sampleResolution);
                sampler = result;
            }
            return result;
        }

        // The point of the surface at the parameters (u, v), with its normal as the intersection gives it
        private Interaction InteractionAt(double u, double v)
        {
            surface.Evaluate(u, v, out SdfVector p, out SdfVector dpdu, out SdfVector dpdv, out Point2f uv, out SdfVector shading);
            Transform renderFromObject = settings.renderFromObject;
            Point3f pr = renderFromObject.Apply(new Point3f((Float)p.x, (Float)p.y, (Float)p.z));
            Vector3f pu = renderFromObject.Apply(new Vector3f((Float)dpdu.x, (Float)dpdu.y, (Float)dpdu.z));
            Vector3f pv = renderFromObject.Apply(new Vector3f((Float)dpdv.x, (Float)dpdv.y, (Float)dpdv.z));
            Normal3f n = pbrt.Normalize((Normal3f)pbrt.Cross(pu, pv));
            if (settings.reverseOrientation ^ settings.transformSwapsHandedness)
            {
                n = -n;
            }
            if (shading.LengthSquared() > 0)
            {
                Normal3f ns = pbrt.Normalize(renderFromObject.Apply(new Normal3f((Float)shading.x, (Float)shading.y, (Float)shading.z)));
                if (settings.reverseOrientation)
                {
                    ns = -ns;
                }
                n = pbrt.FaceForward(n, ns);
            }
            Vector3f pError = pbrt.gamma(8) * pbrt.Abs(new Vector3f(pr.x, pr.y, pr.z));
            return new Interaction(new Point3fi(pr, pError), n, uv);
        }

        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            PatchAreaSampler areaSampler = Sampler();
            areaSampler.Sample(u, out double pu, out double pv, out double density);
            double jacobian = areaSampler.Jacobian(pu, pv);
            if (!(jacobian > 0))
            {
                return default;
            }
            return new ShapeSample(InteractionAt(pu, pv), (Float)(density / jacobian));
        }

        // The area density of the sampling at a point of the surface: the parameters of the point are found by projection, since the density of
        // Sample(u) is not uniform in the area (it follows the cells of the sampler and the Jacobian)
        public override Float PDF(Interaction intr)
        {
            PatchAreaSampler areaSampler = Sampler();
            Point3f p = intr.p();
            double distance = areaSampler.Locate(new SdfVector(p.x, p.y, p.z), out double u, out double v);
            Bounds3f box = Bounds();
            if (distance > 1e-3 * pbrt.Length(box.pMax - box.pMin))
            {
                return 0;
            }
            return (Float)areaSampler.AreaDensity(u, v);
        }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            return PatchSampling.ToSolidAngle(Sample(u), ctx);
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            Ray ray = ctx.SpawnRay(wi);
            if (!Trace(ray, Infinity, out BezierClipHit hit, out double length))
            {
                return 0;
            }
            Interaction at = InteractionAt(hit.u, hit.v);
            double areaPdf = Sampler().AreaDensity(hit.u, hit.v);
            return PatchSampling.AreaToSolidAngle(areaPdf, at.n, at.p(), ctx, wi);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ BezierClipPatchShape face: %d ]", surface.FaceIndex());
        }

        public BezierPatch4 Patch
        {
            get { return patch; }
        }

        public PatchSurface Surface
        {
            get { return surface; }
        }

        // The ray in object space with a direction of unit length; the hit distance is along that direction
        private bool Trace(Ray ray, Float tMax, out BezierClipHit hit, out double length)
        {
            Point3f o = settings.objectFromRender.Apply(ray.o);
            Vector3f d = settings.objectFromRender.Apply(ray.d);
            length = Math.Sqrt((double)d.x * d.x + (double)d.y * d.y + (double)d.z * d.z);
            hit = default;
            if (length == 0 || double.IsNaN(length))
            {
                return false;
            }
            SdfVector origin = new SdfVector(o.x, o.y, o.z);
            SdfVector direction = new SdfVector(d.x / length, d.y / length, d.z / length);
            double tMaxObject = float.IsPositiveInfinity(tMax) ? 1e30 : tMax * length;
            double eps = BezierPatchIntersection.ComputeEpsilon((center - origin).Length(), settings.pixelSpread, settings.epsilon);
            // The hit is found on a leaf of the size eps, so a ray that leaves the surface (a shadow or a bounce ray) must not find its own origin again
            return intersection.Test(origin, direction, 2 * eps, tMaxObject, eps, out hit);
        }

        private SurfaceInteraction InteractionFromHit(Ray ray, BezierClipHit hit)
        {
            double u = hit.u, v = hit.v;
            surface.Evaluate(u, v, out SdfVector p, out SdfVector dpdu, out SdfVector dpdv, out Point2f uv, out SdfVector shading);
            // At a degenerate point (a corner of a patch at an extraordinary vertex) the derivatives vanish: use a point closer to the center
            for (int attempt = 0; attempt < 4; ++attempt)
            {
                double lu = dpdu.LengthSquared(), lv = dpdv.LengthSquared();
                if (SdfVector.Cross(dpdu, dpdv).LengthSquared() > 1e-18 * lu * lv && lu > 0 && lv > 0)
                {
                    break;
                }
                double shrink = 1e-3 * (attempt + 1);
                u += (0.5 - u) * shrink;
                v += (0.5 - v) * shrink;
                surface.Evaluate(u, v, out SdfVector p2, out dpdu, out dpdv, out Point2f uv2, out SdfVector shading2);
            }

            Transform renderFromObject = settings.renderFromObject;
            Point3f pr = renderFromObject.Apply(new Point3f((Float)p.x, (Float)p.y, (Float)p.z));
            Vector3f pu = renderFromObject.Apply(new Vector3f((Float)dpdu.x, (Float)dpdu.y, (Float)dpdu.z));
            Vector3f pv = renderFromObject.Apply(new Vector3f((Float)dpdv.x, (Float)dpdv.y, (Float)dpdv.z));
            bool flipNormal = settings.reverseOrientation ^ settings.transformSwapsHandedness;
            Vector3f pError = pbrt.gamma(8) * pbrt.Abs(new Vector3f(pr.x, pr.y, pr.z));
            SurfaceInteraction si = new SurfaceInteraction(new Point3fi(pr, pError), uv, -ray.d, pu, pv, new Normal3f(0, 0, 0), new Normal3f(0, 0, 0),
                                                           ray.time, flipNormal, surface.FaceIndex());
            if (shading.LengthSquared() > 0)
            {
                // Where the interpolated normal and the surface normal differ much (a PN patch with normals far from those of its surface), a view
                // that sees the surface from the front of its normal but from the back of the interpolated one would shade it black: use the surface
                // normal there, as the rule of Ogaki and Tokuyoshi does for Phong tessellation
                SdfVector surfaceNormal = SdfVector.Cross(dpdu, dpdv);
                if (surfaceNormal.LengthSquared() > 0)
                {
                    surfaceNormal = surfaceNormal.Normalized();
                    SdfVector unitShading = shading.Normalized();
                    if (SdfVector.Dot(surfaceNormal, unitShading) < 0)
                    {
                        surfaceNormal = -surfaceNormal;
                    }
                    Vector3f woObject = settings.objectFromRender.Apply(-ray.d);
                    SdfVector view = new SdfVector(woObject.x, woObject.y, woObject.z);
                    if (SdfVector.Dot(view, surfaceNormal) * SdfVector.Dot(view, unitShading) < 0)
                    {
                        shading = surfaceNormal;
                    }
                }
                Normal3f ns = pbrt.Normalize(renderFromObject.Apply(new Normal3f((Float)shading.x, (Float)shading.y, (Float)shading.z)));
                if (settings.reverseOrientation)
                {
                    ns = -ns;
                }
                PatchShading.Apply(si, ns, pu);
            }
            return si;
        }

        private readonly BezierSurfaceSettings settings;
        private readonly PatchSurface surface;
        private readonly BezierPatch4 patch;
        private readonly BezierPatchIntersection intersection;
        private readonly Bounds3f bounds;
        private readonly SdfVector center;
        private PatchAreaSampler sampler;
    }
}
