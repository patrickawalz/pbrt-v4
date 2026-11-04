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
            SdfVector chord = segment.Position(1) - segment.Position(0);
            reference = Math.Abs(chord.x) <= Math.Abs(chord.y) && Math.Abs(chord.x) <= Math.Abs(chord.z) ? new SdfVector(1, 0, 0) : Math.Abs(chord.y) <= Math.Abs(chord.z) ? new SdfVector(0, 1, 0) : new SdfVector(0, 0, 1);
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

        // The area of the tube (the envelope of the spheres; the caps at the ends are not counted)
        public override Float Area()
        {
            return (Float)Table().area;
        }

        // The tube is sampled, not the caps (the caps of an emitter are reached by the sampling of the directions of the BSDF only, which stays unbiased): a bin of the
        // parameter u by the mass 2 pi r slant, then the angle around the strand uniformly
        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            SamplingTable table = Table();
            if (!(table.total > 0))
            {
                return default;
            }
            int bin = 0;
            double target = u.x * table.total;
            int low = 0, high = table.cumulative.Length - 1;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (table.cumulative[middle + 1] > target)
                {
                    high = middle;
                }
                else
                {
                    low = middle + 1;
                }
            }
            bin = low;
            double mass = table.cumulative[bin + 1] - table.cumulative[bin];
            if (!(mass > 0))
            {
                return default;
            }
            double fraction = Math.Min(1, (target - table.cumulative[bin]) / mass);
            double parameter = (bin + fraction) / table.bins;
            double angle = 2 * Math.PI * u.y;
            if (!Surface(parameter, angle, out Point3f p, out Normal3f n, out double jacobian) || !(jacobian > 0))
            {
                return default;
            }
            double pdf = AreaDensity(table, parameter) / (2 * Math.PI) / jacobian;
            if (!(pdf > 0) || double.IsInfinity(pdf))
            {
                return default;
            }
            Vector3f pError = pbrt.gamma(8) * pbrt.Abs(new Vector3f(p.x, p.y, p.z));
            return new ShapeSample(new Interaction(new Point3fi(p, pError), n, new Point2f((Float)((segment.index + parameter) / segment.count), (Float)u.y)), (Float)pdf);
        }

        public override Float PDF(Interaction intr)
        {
            return 0;
        }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            return PatchSampling.ToSolidAngle(Sample(u), ctx);
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            Ray ray = ctx.SpawnRay(wi);
            if (!Trace(ray, Infinity, out double t, out double u, out SdfVector normal, out SdfVector hitPoint, out double length))
            {
                return 0;
            }
            // The hit of a cap (the sphere at an end, or the disk) is not on the tube
            SdfVector tangent = segment.Velocity(u);
            double speed = tangent.Length();
            if (!(speed > 0))
            {
                return 0;
            }
            tangent = tangent / speed;
            double slope = -segment.RadiusDerivative(u) / speed;
            double along = SdfVector.Dot(normal, tangent);
            const double tolerance = 1e-4;
            if (u <= 1e-9 && along < slope - tolerance || u >= 1 - 1e-9 && along > slope + tolerance || Math.Abs(along - slope) > 0.05)
            {
                return 0;
            }
            SamplingTable table = Table();
            SdfVector e1 = Frame(u, out SdfVector e2);
            double angle = Math.Atan2(SdfVector.Dot(normal, e2), SdfVector.Dot(normal, e1));
            if (angle < 0)
            {
                angle += 2 * Math.PI;
            }
            if (!Surface(u, angle, out Point3f p, out Normal3f n, out double jacobian) || !(jacobian > 0))
            {
                return 0;
            }
            double areaPdf = AreaDensity(table, u) / (2 * Math.PI) / jacobian;
            return PatchSampling.AreaToSolidAngle(areaPdf, n, p, ctx, wi);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ RocapShape segment: %d of %d ]", segment.index, segment.count);
        }

        public RocapSegment Segment
        {
            get { return segment; }
        }

        private sealed class SamplingTable
        {
            public int bins;
            public double[] cumulative;
            public double total;
            public double area;
        }

        // The density of the parameter u: the mass of its bin over the total and the width of the bin
        private static double AreaDensity(SamplingTable table, double u)
        {
            int bin = Math.Max(0, Math.Min(table.bins - 1, (int)(u * table.bins)));
            return (table.cumulative[bin + 1] - table.cumulative[bin]) * table.bins / table.total;
        }

        private SamplingTable Table()
        {
            SamplingTable result = table;
            if (result == null)
            {
                result = new SamplingTable();
                result.bins = 256;
                result.cumulative = new double[result.bins + 1];
                double previous = Mass(0);
                for (int i = 0; i < result.bins; ++i)
                {
                    double next = Mass((i + 1.0) / result.bins);
                    result.cumulative[i + 1] = result.cumulative[i] + 0.5 * (previous + next) / result.bins;
                    previous = next;
                }
                result.total = result.cumulative[result.bins];
                // The exact area of the tube in render space
                double sum = 0;
                const int nu = 128, nv = 16;
                for (int i = 0; i < nu; ++i)
                {
                    for (int j = 0; j < nv; ++j)
                    {
                        if (Surface((i + 0.5) / nu, 2 * Math.PI * (j + 0.5) / nv, out Point3f p, out Normal3f n, out double jacobian))
                        {
                            sum += jacobian;
                        }
                    }
                }
                result.area = sum * (2 * Math.PI / nv) / nu;
                table = result;
            }
            return result;
        }

        // The mass of the density of the parameter u: 2 pi r slant (the area of a ring of the tube)
        private double Mass(double u)
        {
            double speed = segment.Velocity(u).Length();
            double dr = segment.RadiusDerivative(u);
            double slant = Math.Sqrt(Math.Max(0, speed * speed - dr * dr));
            return 2 * Math.PI * Math.Max(0, segment.Radius(u)) * slant;
        }

        // The frame around the tangent at u: the reference axis is the same along the segment, so that the frame turns continuously
        private SdfVector Frame(double u, out SdfVector second)
        {
            SdfVector axis = segment.Velocity(u).Normalized();
            SdfVector first = reference - axis * SdfVector.Dot(reference, axis);
            if (first.LengthSquared() < 1e-12)
            {
                first = Math.Abs(axis.x) < 0.9 ? new SdfVector(1, 0, 0) : new SdfVector(0, 1, 0);
                first = first - axis * SdfVector.Dot(first, axis);
            }
            first = first.Normalized();
            second = SdfVector.Cross(axis, first);
            return first;
        }

        // The point of the envelope of the spheres at (u, angle): (P - q) . q' = -r r' (the normal is -r'/|q'| along the tangent), in render space with the normal and the
        // density of the area per unit of u and angle
        private bool Surface(double u, double angle, out Point3f p, out Normal3f n, out double jacobian)
        {
            p = default;
            n = default;
            jacobian = 0;
            if (!ObjectPoint(u, angle, out SdfVector position, out SdfVector normal))
            {
                return false;
            }
            const double h = 1e-5;
            double u0 = Math.Max(0, u - h), u1 = Math.Min(1, u + h);
            if (!ObjectPoint(u0, angle, out SdfVector a0, out SdfVector n0) || !ObjectPoint(u1, angle, out SdfVector a1, out SdfVector n1) ||
                !ObjectPoint(u, angle - h, out SdfVector b0, out SdfVector m0) || !ObjectPoint(u, angle + h, out SdfVector b1, out SdfVector m1))
            {
                return false;
            }
            Transform renderFromObject = settings.renderFromObject;
            Point3f pr = renderFromObject.Apply(new Point3f((Float)position.x, (Float)position.y, (Float)position.z));
            SdfVector du = ToRender(a1) - ToRender(a0), dv = ToRender(b1) - ToRender(b0);
            du = du / (u1 - u0);
            dv = dv / (2 * h);
            SdfVector cross = SdfVector.Cross(du, dv);
            jacobian = cross.Length();
            if (!(jacobian > 0))
            {
                return false;
            }
            // Outward: away from the center of the sphere at u
            SdfVector center = ToRender(segment.Position(u));
            SdfVector outward = cross.Normalized();
            if (SdfVector.Dot(outward, ToRender(position) - center) < 0)
            {
                outward = -1 * outward;
            }
            if (settings.reverseOrientation ^ settings.transformSwapsHandedness)
            {
                outward = -1 * outward;
            }
            p = pr;
            n = new Normal3f((Float)outward.x, (Float)outward.y, (Float)outward.z);
            return true;
        }

        private SdfVector ToRender(SdfVector point)
        {
            Point3f q = settings.renderFromObject.Apply(new Point3f((Float)point.x, (Float)point.y, (Float)point.z));
            return new SdfVector(q.x, q.y, q.z);
        }

        private bool ObjectPoint(double u, double angle, out SdfVector position, out SdfVector normal)
        {
            position = default;
            normal = default;
            double speed = segment.Velocity(u).Length();
            double radius = segment.Radius(u);
            if (!(speed > 0) || !(radius > 0))
            {
                return false;
            }
            double slope = -segment.RadiusDerivative(u) / speed;
            if (Math.Abs(slope) >= 1 - 1e-9)
            {
                return false;
            }
            SdfVector axis = segment.Velocity(u) / speed;
            SdfVector e1 = Frame(u, out SdfVector e2);
            double c = Math.Sqrt(1 - slope * slope);
            normal = axis * slope + (e1 * Math.Cos(angle) + e2 * Math.Sin(angle)) * c;
            position = segment.Position(u) + normal * radius;
            return true;
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
        private SamplingTable table;
        private SdfVector reference;
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
