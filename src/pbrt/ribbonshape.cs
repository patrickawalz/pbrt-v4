// (not in pbrt-v4) ribbonshape.cs
//
// The shape of one segment of a ribbon (ribbon.cs), and the "ribbon" shape of a scene file.

using System;
using System.Collections.Generic;

namespace pbrt
{
    public sealed unsafe class RibbonShape : Shape
    {
        public RibbonShape(BezierSurfaceSettings settings, RibbonSegment segment)
        {
            this.settings = settings;
            this.segment = segment;
            segment.Bounds(out SdfVector low, out SdfVector high);
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
            if (!Trace(ray, tMax, out double u, out double v, out double t, out double length))
            {
                return default;
            }
            SurfaceInteraction intr = InteractionAt(u, v, ray);
            return new ShapeIntersection(intr, (Float)(t / length));
        }

        public override bool IntersectP(Ray ray, Float tMax = Infinity)
        {
            return Trace(ray, tMax, out double u, out double v, out double t, out double length);
        }

        public override Float Area()
        {
            return (Float)Sampler().Area;
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
            return new ShapeSample(PointAt(pu, 2 * pv - 1).intr, (Float)(density / jacobian));
        }

        public override Float PDF(Interaction intr)
        {
            return 1 / Area();
        }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            return PatchSampling.ToSolidAngle(Sample(u), ctx);
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            Ray ray = ctx.SpawnRay(wi);
            if (!Trace(ray, Infinity, out double u, out double v, out double t, out double length))
            {
                return 0;
            }
            Interaction at = PointAt(u, v).intr;
            double areaPdf = Sampler().AreaDensity(u, 0.5 * (v + 1));
            return PatchSampling.AreaToSolidAngle(areaPdf, at.n, at.p(), ctx, wi);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ RibbonShape segment: %d of %d ]", segment.index, segment.count);
        }

        public RibbonSegment Segment
        {
            get { return segment; }
        }

        // The ray in object space with a direction of unit length; the hit distance is along that direction
        private bool Trace(Ray ray, Float tMax, out double u, out double v, out double t, out double length)
        {
            u = 0;
            v = 0;
            t = 0;
            Point3f o = settings.objectFromRender.Apply(ray.o);
            Vector3f d = settings.objectFromRender.Apply(ray.d);
            length = Math.Sqrt((double)d.x * d.x + (double)d.y * d.y + (double)d.z * d.z);
            if (length == 0 || double.IsNaN(length))
            {
                return false;
            }
            SdfVector origin = new SdfVector(o.x, o.y, o.z);
            SdfVector direction = new SdfVector(d.x / length, d.y / length, d.z / length);
            double tMaxObject = float.IsPositiveInfinity(tMax) ? 1e30 : tMax * length;
            return segment.Intersect(origin, direction, 0, tMaxObject, settings.epsilon, out t, out u, out v);
        }

        private PatchAreaSampler Sampler()
        {
            PatchAreaSampler result = sampler;
            if (result == null)
            {
                result = new PatchAreaSampler((double u, double v) =>
                {
                    SdfVector p = segment.Position(u, 2 * v - 1);
                    Point3f q = settings.renderFromObject.Apply(new Point3f((Float)p.x, (Float)p.y, (Float)p.z));
                    return new SdfVector(q.x, q.y, q.z);
                }, 16);
                sampler = result;
            }
            return result;
        }

        // The geometry at the parameters (u, v), v in [-1, 1]: the position in render space, the derivatives and the texture coordinates
        private void Geometry(double u, double v, out Point3f p, out Vector3f dpdu, out Vector3f dpdv, out Point2f uv)
        {
            SdfVector position = segment.Position(u, v);
            segment.Derivatives(u, v, out SdfVector du, out SdfVector dv);
            Transform renderFromObject = settings.renderFromObject;
            p = renderFromObject.Apply(new Point3f((Float)position.x, (Float)position.y, (Float)position.z));
            // The texture coordinates run along the whole ribbon and across its width
            dpdu = renderFromObject.Apply(new Vector3f((Float)(du.x * segment.count), (Float)(du.y * segment.count), (Float)(du.z * segment.count)));
            dpdv = renderFromObject.Apply(new Vector3f((Float)(dv.x * 2), (Float)(dv.y * 2), (Float)(dv.z * 2)));
            uv = new Point2f((Float)((segment.index + u) / segment.count), (Float)(0.5 * (v + 1)));
        }

        private SurfaceInteraction InteractionAt(double u, double v, Ray ray)
        {
            Geometry(u, v, out Point3f p, out Vector3f dpdu, out Vector3f dpdv, out Point2f uv);
            bool flipNormal = settings.reverseOrientation ^ settings.transformSwapsHandedness;
            Vector3f pError = pbrt.gamma(8) * pbrt.Abs(new Vector3f(p.x, p.y, p.z));
            return new SurfaceInteraction(new Point3fi(p, pError), uv, -ray.d, dpdu, dpdv, new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), ray.time, flipNormal,
                                          segment.index);
        }

        // The sample of the point at (u, v), with the normal of the surface (the area density is set by the caller)
        private ShapeSample PointAt(double u, double v)
        {
            Geometry(u, v, out Point3f p, out Vector3f dpdu, out Vector3f dpdv, out Point2f uv);
            Normal3f n = pbrt.Normalize((Normal3f)pbrt.Cross(dpdu, dpdv));
            if (settings.reverseOrientation ^ settings.transformSwapsHandedness)
            {
                n = -n;
            }
            Vector3f pError = pbrt.gamma(8) * pbrt.Abs(new Vector3f(p.x, p.y, p.z));
            return new ShapeSample(new Interaction(new Point3fi(p, pError), n, uv), 0);
        }

        private readonly BezierSurfaceSettings settings;
        private readonly RibbonSegment segment;
        private readonly Bounds3f bounds;
        private PatchAreaSampler sampler;
    }

    public static class Ribbons
    {
        // "point3 P" (the control points of the quadratic B-spline, at least three), "normal N" (the normal of the surface at each point; the normal of the plane of
        // the curve when absent), "float width" (the half width at each point, or one value for all), "epsilon" (a fraction of the diagonal of the points that
        // keeps rays that leave the surface from hitting it again)
        public static pstd.vector<Shape> Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation, ParameterDictionary parameters,
                                                FileLoc loc)
        {
            List<Point3f> pointList = parameters.GetPoint3fArray("P");
            List<Normal3f> normalList = parameters.GetNormal3fArray("N");
            List<float> widthList = parameters.GetFloatArray("width");
            if (pointList.Count < 3)
            {
                pbrt.ErrorExit(loc, "ribbon: at least three points \"P\" are needed.");
            }
            if (normalList.Count != 0 && normalList.Count != pointList.Count)
            {
                pbrt.ErrorExit(loc, "ribbon: \"N\" needs a normal for each point.");
            }
            if (widthList.Count != 0 && widthList.Count != 1 && widthList.Count != pointList.Count)
            {
                pbrt.ErrorExit(loc, "ribbon: \"width\" needs one value or one for each point.");
            }
            SdfVector[] points = new SdfVector[pointList.Count];
            SdfVector minimum = new SdfVector(double.MaxValue, double.MaxValue, double.MaxValue);
            SdfVector maximum = new SdfVector(-double.MaxValue, -double.MaxValue, -double.MaxValue);
            for (int i = 0; i < points.Length; ++i)
            {
                points[i] = new SdfVector(pointList[i].x, pointList[i].y, pointList[i].z);
                minimum = new SdfVector(Math.Min(minimum.x, points[i].x), Math.Min(minimum.y, points[i].y), Math.Min(minimum.z, points[i].z));
                maximum = new SdfVector(Math.Max(maximum.x, points[i].x), Math.Max(maximum.y, points[i].y), Math.Max(maximum.z, points[i].z));
            }
            double diagonal = (maximum - minimum).Length();
            SdfVector[] normals = null;
            if (normalList.Count != 0)
            {
                normals = new SdfVector[normalList.Count];
                for (int i = 0; i < normals.Length; ++i)
                {
                    normals[i] = new SdfVector(normalList[i].x, normalList[i].y, normalList[i].z);
                }
            }
            double[] widths = new double[points.Length];
            for (int i = 0; i < widths.Length; ++i)
            {
                widths[i] = widthList.Count == 0 ? 0.05 * diagonal : widthList.Count == 1 ? widthList[0] : widthList[i];
            }

            BezierSurfaceSettings settings = new BezierSurfaceSettings();
            settings.renderFromObject = renderFromObject;
            settings.objectFromRender = objectFromRender;
            settings.reverseOrientation = reverseOrientation;
            settings.transformSwapsHandedness = renderFromObject.SwapsHandedness();
            settings.epsilon = parameters.GetOneFloat("epsilon", 1e-5f) * diagonal;

            pstd.vector<Shape> shapes = new pstd.vector<Shape>();
            foreach (RibbonSegment segment in RibbonBuilder.Build(points, normals, widths))
            {
                shapes.push_back(new RibbonShape(settings, segment));
            }
            return shapes;
        }
    }
}
