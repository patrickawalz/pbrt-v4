// (not in pbrt-v4) rocapshape.cs
//
// The shape of one segment of a hair strand (rocap.cs), and the "rocap" shape of a scene file.

using System;
using System.Collections.Generic;

namespace pbrt
{
    public sealed unsafe class RocapShape : Shape
    {
        public RocapShape(BezierSurfaceSettings settings, RocapSegment segment)
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
            if (!Trace(ray, tMax, out double t, out double u, out SdfVector normal, out SdfVector hitPoint, out double length))
            {
                return default;
            }
            return new ShapeIntersection(InteractionAt(u, normal, hitPoint, ray), (Float)(t / length));
        }

        public override bool IntersectP(Ray ray, Float tMax = Infinity)
        {
            return Trace(ray, tMax, out double t, out double u, out SdfVector normal, out SdfVector hitPoint, out double length);
        }

        // The area of the surface of revolution of the strand (the caps of the spheres at the ends are not counted)
        public override Float Area()
        {
            double sum = 0;
            const int n = 64;
            for (int i = 0; i < n; ++i)
            {
                double u = (i + 0.5) / n;
                double speed = segment.Velocity(u).Length();
                double dr = segment.RadiusDerivative(u);
                double slant = Math.Sqrt(Math.Max(0, speed * speed - dr * dr));
                sum += 2 * Math.PI * Math.Max(0, segment.Radius(u)) * slant;
            }
            return (Float)(sum / n);
        }

        // Strands are not sampled as lights
        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            return default;
        }

        public override Float PDF(Interaction intr)
        {
            return 0;
        }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            return default;
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            return 0;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ RocapShape segment: %d of %d ]", segment.index, segment.count);
        }

        public RocapSegment Segment
        {
            get { return segment; }
        }

        private bool Trace(Ray ray, Float tMax, out double t, out double u, out SdfVector normal, out SdfVector hitPoint, out double length)
        {
            hitPoint = default;
            t = 0;
            u = 0;
            normal = default;
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
            if (!segment.Intersect(origin, direction, settings.epsilon, tMaxObject, out t, out u, out normal))
            {
                return false;
            }
            hitPoint = origin + direction * t;
            return true;
        }

        private SurfaceInteraction InteractionAt(double u, SdfVector normalObject, SdfVector position, Ray ray)
        {
            Transform renderFromObject = settings.renderFromObject;
            SdfVector tangent = segment.Velocity(u);
            // The tangent of the surface in the direction of the strand
            SdfVector along = tangent - normalObject * SdfVector.Dot(tangent, normalObject);
            if (along.LengthSquared() < 1e-24 * Math.Max(1e-300, tangent.LengthSquared()) || along.LengthSquared() == 0)
            {
                SdfVector helper = Math.Abs(normalObject.x) < 0.9 ? new SdfVector(1, 0, 0) : new SdfVector(0, 1, 0);
                along = SdfVector.Cross(normalObject, helper);
            }
            // Around the strand: the angle in the plane perpendicular to the tangent
            SdfVector axis = tangent.LengthSquared() > 0 ? tangent.Normalized() : new SdfVector(0, 0, 1);
            SdfVector reference = Math.Abs(axis.x) < 0.9 ? new SdfVector(1, 0, 0) : new SdfVector(0, 1, 0);
            SdfVector side = SdfVector.Cross(axis, reference).Normalized();
            SdfVector up = SdfVector.Cross(axis, side);
            double angle = Math.Atan2(SdfVector.Dot(normalObject, up), SdfVector.Dot(normalObject, side));
            SdfVector around = SdfVector.Cross(normalObject, along);
            double radius = Math.Max(Math.Max(0, segment.Radius(u)), 1e-12);
            around = around.Normalized() * (2 * Math.PI * radius);

            Point3f p = renderFromObject.Apply(new Point3f((Float)position.x, (Float)position.y, (Float)position.z));
            Vector3f dpdu = renderFromObject.Apply(new Vector3f((Float)(along.x * segment.count), (Float)(along.y * segment.count), (Float)(along.z * segment.count)));
            Vector3f dpdv = renderFromObject.Apply(new Vector3f((Float)around.x, (Float)around.y, (Float)around.z));
            Point2f uv = new Point2f((Float)((segment.index + u) / segment.count), (Float)(0.5 + angle / (2 * Math.PI)));
            bool flipNormal = settings.reverseOrientation ^ settings.transformSwapsHandedness;
            Vector3f pError = pbrt.gamma(8) * pbrt.Abs(new Vector3f(p.x, p.y, p.z));
            return new SurfaceInteraction(new Point3fi(p, pError), uv, -ray.d, dpdu, dpdv, new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), ray.time, flipNormal,
                                          segment.index);
        }

        private readonly BezierSurfaceSettings settings;
        private readonly RocapSegment segment;
        private readonly Bounds3f bounds;
    }

    public static class Rocaps
    {
        // "point3 P" (the control points of the chain of cubic Bezier curves: 3n + 1 points), "float radius" (one value, or one for each point),
        // "float width0" and "float width1" (diameters at the two ends when "radius" is absent, 0.02 and 0.02 of the diagonal), "bool flatcaps" (disks at the ends instead of spheres), "epsilon" (a fraction of the
        // diagonal of the points that keeps rays that leave the surface from hitting it again)
        public static pstd.vector<Shape> Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation, ParameterDictionary parameters,
                                                FileLoc loc)
        {
            List<Point3f> pointList = parameters.GetPoint3fArray("P");
            List<float> radiusList = parameters.GetFloatArray("radius");
            if (pointList.Count < 4 || (pointList.Count - 1) % 3 != 0)
            {
                pbrt.ErrorExit(loc, "rocap: \"P\" needs 3n + 1 control points (n cubic curves), n >= 1.");
            }
            if (radiusList.Count > 1 && radiusList.Count != pointList.Count)
            {
                pbrt.ErrorExit(loc, "rocap: \"radius\" needs one value or one for each point.");
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
            double[] radii = new double[points.Length];
            double width0 = parameters.GetOneFloat("width0", (float)(0.02 * diagonal));
            double width1 = parameters.GetOneFloat("width1", (float)(0.02 * diagonal));
            for (int i = 0; i < radii.Length; ++i)
            {
                if (radiusList.Count == 1)
                {
                    radii[i] = radiusList[0];
                }
                else if (radiusList.Count > 1)
                {
                    radii[i] = radiusList[i];
                }
                else
                {
                    radii[i] = 0.5 * (width0 + (width1 - width0) * i / (radii.Length - 1));
                }
            }

            BezierSurfaceSettings settings = new BezierSurfaceSettings();
            settings.renderFromObject = renderFromObject;
            settings.objectFromRender = objectFromRender;
            settings.reverseOrientation = reverseOrientation;
            settings.transformSwapsHandedness = renderFromObject.SwapsHandedness();
            settings.epsilon = parameters.GetOneFloat("epsilon", 1e-6f) * diagonal;

            bool flatCaps = parameters.GetOneBool("flatcaps", false);
            int count = (points.Length - 1) / 3;
            pstd.vector<Shape> shapes = new pstd.vector<Shape>();
            for (int s = 0; s < count; ++s)
            {
                SdfVector[] p = new SdfVector[] { points[3 * s], points[3 * s + 1], points[3 * s + 2], points[3 * s + 3] };
                double[] r = new double[] { radii[3 * s], radii[3 * s + 1], radii[3 * s + 2], radii[3 * s + 3] };
                shapes.push_back(new RocapShape(settings, new RocapSegment(p, r, s, count) { FlatCaps = flatCaps }));
            }
            return shapes;
        }
    }
}
