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
            intersection.Options = options;
            SdfVector low = intersection.BoundsMin, high = intersection.BoundsMax;
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
            // The area of a 8 x 8 grid of the surface
            int n = 8;
            SdfVector[] grid = new SdfVector[(n + 1) * (n + 1)];
            for (int j = 0; j <= n; ++j)
            {
                for (int i = 0; i <= n; ++i)
                {
                    surface.Evaluate((double)i / n, (double)j / n, out SdfVector p, out SdfVector dpdu, out SdfVector dpdv, out Point2f uv, out SdfVector ns);
                    Point3f q = settings.renderFromObject.Apply(new Point3f((Float)p.x, (Float)p.y, (Float)p.z));
                    grid[j * (n + 1) + i] = new SdfVector(q.x, q.y, q.z);
                }
            }
            double area = 0;
            for (int j = 0; j < n; ++j)
            {
                for (int i = 0; i < n; ++i)
                {
                    SdfVector a = grid[j * (n + 1) + i], b = grid[j * (n + 1) + i + 1], c = grid[(j + 1) * (n + 1) + i], d = grid[(j + 1) * (n + 1) + i + 1];
                    area += 0.5 * SdfVector.Cross(b - a, c - a).Length() + 0.5 * SdfVector.Cross(b - d, c - d).Length();
                }
            }
            return (Float)area;
        }

        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            pbrt.ErrorExit("Bezier clipped patches: area lights are not supported yet.");
            return default;
        }

        public override Float PDF(Interaction intr)
        {
            pbrt.ErrorExit("Bezier clipped patches: area lights are not supported yet.");
            return 0;
        }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            pbrt.ErrorExit("Bezier clipped patches: area lights are not supported yet.");
            return default;
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            pbrt.ErrorExit("Bezier clipped patches: area lights are not supported yet.");
            return 0;
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
            return intersection.Test(origin, direction, 0, tMaxObject, eps, out hit);
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
                Normal3f ns = pbrt.Normalize(renderFromObject.Apply(new Normal3f((Float)shading.x, (Float)shading.y, (Float)shading.z)));
                if (settings.reverseOrientation)
                {
                    ns = -ns;
                }
                si.SetShadingGeometry(ns, pu, pv, new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), true);
            }
            return si;
        }

        private readonly BezierSurfaceSettings settings;
        private readonly PatchSurface surface;
        private readonly BezierPatch4 patch;
        private readonly BezierPatchIntersection intersection;
        private readonly Bounds3f bounds;
        private readonly SdfVector center;
    }
}
