// (not in pbrt-v4) ribbon2shape.cs
//
// The "ribbon2" shape: the curved ribbons of Reshetov (ribbon.cs) with more ways to give the centerline and with better sampling
// as a light. The segments and their ray intersection are those of "ribbon" (RibbonSegment); "ribbon" itself is as it was.
//
//   Shape "ribbon2" "point3 P" [...] ("string basis" "b-spline") ("integer degree" 2) ("integer step" ...) ("integer pieces" 4)
//         ("normal N" [...]) ("float width" [...]) ("rgb Cs" [...]) ("float epsilon" 1e-5) ("bool solidanglesampling" true)
//
//   basis, degree:
//     "b-spline" with the degree 2 (the default): the quadratic B-spline of "ribbon", the same segments.
//     "bezier" with the degree 2: a chain of quadratic Bezier curves, 2n + 1 points (the ends and the middle point of each curve).
//       Both of these are the centerline of the paper itself: nothing is approximated.
//     "bezier", "b-spline" with the degree 3, and "catmull-rom", "hermite", "power" (the cubic bases of RenderMan, with "step" as for
//       its curves: 4 + n step points): the centerline of the paper is quadratic, so that the ray intersection is a cubic equation,
//       and a cubic curve is not a quadratic one. Each cubic curve is replaced by "pieces" quadratic curves: the curve is cut at
//       equal steps h of its parameter, and the quadratic B-spline is taken whose control point for a piece from t to t + h is the
//       mean of C(t) + h C'(t) / 2 and C(t + h) - h C'(t + h) / 2 (the two are the same point for a quadratic, and h^3 C''' / 12
//       apart for a cubic). Being a B-spline, the pieces meet with one tangent, and the ribbon is as smooth as that of "ribbon"; it
//       is an approximation of the cubic curve, with an error that falls with the cube of 1 / pieces (ribbon2_test.cs prints it).
//       Where two cubic curves do not meet, or meet at an angle, the ribbon is cut and started again.
//   N, width, Cs: one for each point of "P" (or one width, one color), following the same basis as the points; "width" is the half width.
//
// As a light. "ribbon" is sampled by area. From a lit point x this shape chooses the position u along the ribbon by an estimate
// of the light that the ribbon sends to x from there, W(u) = J(u) |n . (x - P)| / |x - P|^3 at the centerline (J the area for a
// step of the parameters, n the normal), at Nodes + 1 values of u joined by lines: that takes out the fall-off with the distance
// and the foreshortening of a ribbon seen along its length or at a grazing angle (not from a paper; the same idea as for
// "rocap"). Across the width the position is uniform. A tenth of the samples are those of the area, so that every point has a
// density. PDF(Interaction) is the density of the samples by area at the point ("ribbon" gives 1 / area, which its samples only
// have on average).

using System;
using System.Collections.Generic;

namespace pbrt
{
    // Ribbon2Shape Definition
    public sealed unsafe class Ribbon2Shape : Shape
    {
        // Ribbon2Shape Public Methods
        public Ribbon2Shape(BezierSurfaceSettings settings, RibbonSegment segment, int strandColors, bool solidAngleSampling)
        {
            this.strandColors = strandColors;
            this.settings = settings;
            this.segment = segment;
            this.solidAngleSampling = solidAngleSampling;
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
            SquareMatrix4 m = settings.renderFromObject.GetMatrix();
            matrix = new double[12];
            for (int row = 0; row < 3; ++row)
            {
                for (int column = 0; column < 4; ++column)
                {
                    matrix[4 * row + column] = m[row][column];
                }
            }
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

        // The density of Sample(u) at a point of the ribbon, by area: from the parameters of the point
        public override Float PDF(Interaction intr)
        {
            double u = Math.Max(0, Math.Min(1, (double)intr.uv.x * segment.count - segment.index));
            double v = Math.Max(0, Math.Min(1, (double)intr.uv.y));
            double pdf = Sampler().AreaDensity(u, v);
            return double.IsNaN(pdf) || double.IsInfinity(pdf) ? 0 : (Float)pdf;
        }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            PatchAreaSampler areaSampler = Sampler();
            Span<double> weights = stackalloc double[Nodes + 1];
            double total = solidAngleSampling ? Weights(ctx.p(), weights) : 0;
            if (!(total > 0))
            {
                return PatchSampling.ToSolidAngle(Sample(u), ctx);
            }
            double pu, pv;
            if (u.x < Defence)
            {
                areaSampler.Sample(new Point2f((Float)Math.Min(u.x / Defence, 1 - 1e-7), u.y), out pu, out pv, out double areaDensity);
            }
            else
            {
                pu = AlongByWeights(weights, total, Math.Min((u.x - Defence) / (1 - Defence), 1 - 1e-12));
                pv = Math.Min((double)u.y, 1 - 1e-12);
            }
            double jacobian = areaSampler.Jacobian(pu, pv);
            if (!(jacobian > 0))
            {
                return default;
            }
            ShapeSample sample = PointAt(pu, 2 * pv - 1);
            sample.pdf = (Float)(ParameterDensity(areaSampler, weights, total, pu, pv) / jacobian);
            return PatchSampling.ToSolidAngle(sample, ctx);
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            Ray ray = ctx.SpawnRay(wi);
            if (!Trace(ray, Infinity, out double u, out double v, out double t, out double length))
            {
                return 0;
            }
            PatchAreaSampler areaSampler = Sampler();
            Interaction at = PointAt(u, v).intr;
            double across = 0.5 * (v + 1);
            Span<double> weights = stackalloc double[Nodes + 1];
            double total = solidAngleSampling ? Weights(ctx.p(), weights) : 0;
            double areaPdf;
            if (total > 0)
            {
                double jacobian = areaSampler.Jacobian(u, across);
                if (!(jacobian > 0))
                {
                    return 0;
                }
                areaPdf = ParameterDensity(areaSampler, weights, total, u, across) / jacobian;
            }
            else
            {
                areaPdf = areaSampler.AreaDensity(u, across);
            }
            return PatchSampling.AreaToSolidAngle(areaPdf, at.n, at.p(), ctx, wi);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ Ribbon2Shape segment: %d of %d ]", segment.Index, segment.Count);
        }

        public RibbonSegment Segment
        {
            get { return segment; }
        }

        // Ribbon2Shape Private Methods
        // The number of intervals of u that the weights of a lit point are taken at the ends of
        private const int Nodes = 16;

        // The share of the samples for a lit point that are those of the area
        private const double Defence = 0.1;

        // The weights W(u) at the nodes for the lit point (render space), and their integral over u
        private double Weights(Point3f lit, Span<double> weights)
        {
            PatchAreaSampler areaSampler = Sampler();
            SdfVector x = new SdfVector(lit.x, lit.y, lit.z);
            for (int i = 0; i <= Nodes; ++i)
            {
                double u = (double)i / Nodes;
                areaSampler.Differences(u, 0.5, out SdfVector du, out SdfVector dv);
                SdfVector normal = SdfVector.Cross(du, dv);
                double jacobian = normal.Length();
                SdfVector toLit = x - Position(u, 0.5);
                double distance = toLit.Length();
                // J |n . D| / |D|^3 with the unit normal n: the cross product of the derivatives is J n
                weights[i] = jacobian > 0 && distance > 0 ? Math.Abs(SdfVector.Dot(normal, toLit)) / (distance * distance * distance) : 0;
            }
            double total = 0;
            for (int i = 0; i < Nodes; ++i)
            {
                total += 0.5 * (weights[i] + weights[i + 1]) / Nodes;
            }
            return double.IsNaN(total) || double.IsInfinity(total) ? 0 : total;
        }

        // The parameter u for a number in [0, 1), with the density of the weights joined by lines
        private static double AlongByWeights(Span<double> weights, double total, double choice)
        {
            double target = choice * total;
            for (int i = 0; i < Nodes; ++i)
            {
                double mass = 0.5 * (weights[i] + weights[i + 1]) / Nodes;
                if (target < mass || i == Nodes - 1)
                {
                    double a = weights[i], b = weights[i + 1];
                    double x = mass > 0 ? Math.Max(0, Math.Min(1, target / mass)) : 0.5;
                    double within = Math.Abs(b - a) <= 1e-12 * (a + b) ? x : (Math.Sqrt(Math.Max(0, a * a + (b * b - a * a) * x)) - a) / (b - a);
                    return Math.Min((i + Math.Max(0, Math.Min(1, within))) / Nodes, 1 - 1e-12);
                }
                target -= mass;
            }
            return 0.5;
        }

        // The density of the parameters (u, v) in [0, 1]^2 of the samples for a lit point
        private static double ParameterDensity(PatchAreaSampler areaSampler, Span<double> weights, double total, double u, double v)
        {
            double position = Math.Max(0, Math.Min(1, u)) * Nodes;
            int bin = Math.Min(Nodes - 1, (int)position);
            double within = position - bin;
            return (1 - Defence) * ((1 - within) * weights[bin] + within * weights[bin + 1]) / total + Defence * areaSampler.Density(u, v);
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

        // The point of the ribbon at (u, v) in [0, 1]^2 in render space, in double precision (the densities are from
        // differences of such points)
        private SdfVector Position(double u, double v)
        {
            SdfVector p = segment.Position(u, 2 * v - 1);
            return new SdfVector(matrix[0] * p.x + matrix[1] * p.y + matrix[2] * p.z + matrix[3], matrix[4] * p.x + matrix[5] * p.y + matrix[6] * p.z + matrix[7],
                                 matrix[8] * p.x + matrix[9] * p.y + matrix[10] * p.z + matrix[11]);
        }

        private PatchAreaSampler Sampler()
        {
            PatchAreaSampler result = sampler;
            if (result == null)
            {
                result = new PatchAreaSampler(Position, 16);
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
            dpdu = renderFromObject.Apply(new Vector3f((Float)(du.x * segment.Count), (Float)(du.y * segment.Count), (Float)(du.z * segment.Count)));
            dpdv = renderFromObject.Apply(new Vector3f((Float)(dv.x * 2), (Float)(dv.y * 2), (Float)(dv.z * 2)));
            uv = new Point2f((Float)((segment.Index + u) / segment.Count), (Float)(0.5 * (v + 1)));
        }

        private SurfaceInteraction InteractionAt(double u, double v, Ray ray)
        {
            Geometry(u, v, out Point3f p, out Vector3f dpdu, out Vector3f dpdv, out Point2f uv);
            bool flipNormal = settings.reverseOrientation ^ settings.transformSwapsHandedness;
            Vector3f pError = pbrt.gamma(8) * pbrt.Abs(new Vector3f(p.x, p.y, p.z));
            return new SurfaceInteraction(new Point3fi(p, pError), uv, -ray.d, dpdu, dpdv, new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), ray.time, flipNormal,
                                          strandColors);
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

        // Ribbon2Shape Private Members
        private readonly BezierSurfaceSettings settings;
        private readonly RibbonSegment segment;
        // The number of the registered colors of the strand (StrandColors), the face index of the hits; 0 without
        private readonly int strandColors;
        private readonly bool solidAngleSampling;
        private readonly Bounds3f bounds;
        private readonly double[] matrix;
        private PatchAreaSampler sampler;
    }

    // Ribbons2 Definition
    public static class Ribbons2
    {
        // One run of a ribbon: n quadratic Bezier curves that follow each other, as the 2n + 1 points of their ends and middle
        // points, with the half width at each of those and the normal of the surface at the n + 1 ends (or null)
        public sealed class Run
        {
            public SdfVector[] points;
            public double[] widths;
            public SdfVector[] normals;
        }

        // The run of a quadratic B-spline (the centerline of "ribbon"): the curves end at the middles of the control points,
        // and at the first and the last one. normals: one for each control point, or null.
        public static Run FromSpline(SdfVector[] points, double[] widths, SdfVector[] normals)
        {
            int curves = points.Length - 2;
            Run run = new Run();
            run.points = new SdfVector[2 * curves + 1];
            run.widths = new double[2 * curves + 1];
            run.normals = normals == null ? null : new SdfVector[curves + 1];
            for (int j = 0; j <= curves; ++j)
            {
                bool first = j == 0, last = j == curves;
                run.points[2 * j] = first ? points[0] : (last ? points[points.Length - 1] : (points[j] + points[j + 1]) * 0.5);
                run.widths[2 * j] = first ? widths[0] : (last ? widths[points.Length - 1] : (widths[j] + widths[j + 1]) * 0.5);
                if (normals != null)
                {
                    run.normals[j] = first ? normals[0] : (last ? normals[points.Length - 1] : (normals[j] + normals[j + 1]) * 0.5);
                }
                if (!last)
                {
                    run.points[2 * j + 1] = points[j + 1];
                    run.widths[2 * j + 1] = widths[j + 1];
                }
            }
            return run;
        }

        // The control points of the quadratic B-spline that stands for cubic Bezier curves that follow each other with one
        // tangent (four values each in `cubic`, `pieces` quadratic curves for each): the first point, one point for each
        // piece, the last point. For a piece from t to t + h the point is the mean of C(t) + h C'(t) / 2 and
        // C(t + h) - h C'(t + h) / 2. `plain` values (widths, the components of normals and colors) are taken at the middle
        // of the piece instead.
        public static double[] SplineOfCubics(double[] cubic, int pieces, bool plain)
        {
            int curves = cubic.Length / 4;
            double[] spline = new double[curves * pieces + 2];
            spline[0] = cubic[0];
            spline[spline.Length - 1] = cubic[cubic.Length - 1];
            double h = 1.0 / pieces;
            for (int s = 0; s < curves; ++s)
            {
                for (int k = 0; k < pieces; ++k)
                {
                    double t = k * h;
                    spline[1 + s * pieces + k] = plain ? Cubic(cubic, s, t + 0.5 * h) :
                        0.5 * (Cubic(cubic, s, t) + 0.5 * h * CubicDerivative(cubic, s, t) + Cubic(cubic, s, t + h) - 0.5 * h * CubicDerivative(cubic, s, t + h));
                }
            }
            return spline;
        }

        private static double Cubic(double[] cubic, int curve, double t)
        {
            double s = 1 - t;
            return cubic[4 * curve] * (s * s * s) + cubic[4 * curve + 1] * (3 * t * s * s) + cubic[4 * curve + 2] * (3 * t * t * s) + cubic[4 * curve + 3] * (t * t * t);
        }

        private static double CubicDerivative(double[] cubic, int curve, double t)
        {
            double s = 1 - t;
            return 3 * ((cubic[4 * curve + 1] - cubic[4 * curve]) * (s * s) + (cubic[4 * curve + 2] - cubic[4 * curve + 1]) * (2 * t * s) +
                        (cubic[4 * curve + 3] - cubic[4 * curve + 2]) * (t * t));
        }

        // The segments of the runs of one ribbon, numbered along the whole ribbon. Within a run the bitangent is carried from
        // one curve to the next, and where the two curves have the same derivative at their joint (as those of a B-spline
        // have) the surface normals are made the same along the joint (RibbonBuilder.Connect, Section 4.1 of the paper).
        public static List<RibbonSegment> Build(List<Run> runs)
        {
            List<RibbonSegment> segments = new List<RibbonSegment>();
            int total = 0;
            foreach (Run run in runs)
            {
                total += (run.points.Length - 1) / 2;
            }
            foreach (Run run in runs)
            {
                int curves = (run.points.Length - 1) / 2;
                SdfVector[] normals = run.normals ?? PlaneNormals(run.points, curves);
                int first = segments.Count;
                for (int i = 0; i < curves; ++i)
                {
                    RibbonSegment segment = new RibbonSegment();
                    segment.index = first + i;
                    segment.count = total;
                    segment.b0 = run.points[2 * i];
                    segment.b1 = run.points[2 * i + 1];
                    segment.b2 = run.points[2 * i + 2];
                    segment.width0 = run.widths[2 * i];
                    segment.width1 = run.widths[2 * i + 1];
                    segment.width2 = run.widths[2 * i + 2];
                    SdfVector leaving = segment.b1 - segment.b0, arriving = segment.b2 - segment.b1;
                    SdfVector start = Bitangent(leaving, normals[i]), end = Bitangent(arriving, normals[i + 1]);
                    bool smooth = false;
                    if (i > 0)
                    {
                        // The bitangent of the end of the curve before, unless the centerline turns at the joint
                        RibbonSegment previous = segments[first + i - 1];
                        SdfVector before = previous.b2 - previous.b1;
                        double turn = SdfVector.Cross(before, leaving).Length(), scale = before.Length() * leaving.Length();
                        if (turn <= 1e-3 * scale && SdfVector.Dot(before, leaving) > 0)
                        {
                            start = previous.bitangent1.Normalized();
                            smooth = (before - leaving).Length() <= 1e-6 * Math.Sqrt(scale);
                        }
                    }
                    if (SdfVector.Dot(start, end) < 0)
                    {
                        end = -end;
                    }
                    segment.bitangent0 = start;
                    segment.bitangent1 = end;
                    segment.beta = 0;
                    segment.gamma = 0;
                    if (smooth)
                    {
                        RibbonBuilder.Connect(segments[first + i - 1], segment);
                    }
                    segments.Add(segment);
                }
            }
            return segments;
        }

        private static SdfVector Bitangent(SdfVector tangent, SdfVector normal)
        {
            SdfVector bitangent = SdfVector.Cross(tangent, normal);
            return bitangent.LengthSquared() > 0 ? bitangent.Normalized() : RibbonBuilder.OrthogonalTo(tangent);
        }

        // The normal at every end of the curves of a run without normals: the mean of the normals of the planes of the two
        // curves that meet there, with one side for all; a straight curve takes the normal of its neighbor
        private static SdfVector[] PlaneNormals(SdfVector[] points, int curves)
        {
            SdfVector[] planes = new SdfVector[curves];
            bool[] valid = new bool[curves];
            for (int i = 0; i < curves; ++i)
            {
                SdfVector a = points[2 * i + 1] - points[2 * i], b = points[2 * i + 2] - points[2 * i + 1];
                SdfVector normal = SdfVector.Cross(a, b);
                valid[i] = normal.LengthSquared() > 1e-24 * a.LengthSquared() * b.LengthSquared();
                planes[i] = valid[i] ? normal.Normalized() : new SdfVector(0, 0, 0);
            }
            for (int i = 1; i < curves; ++i)
            {
                if (valid[i] && valid[i - 1] && SdfVector.Dot(planes[i], planes[i - 1]) < 0)
                {
                    planes[i] = -planes[i];
                }
                else if (!valid[i] && valid[i - 1])
                {
                    planes[i] = planes[i - 1];
                    valid[i] = true;
                }
            }
            for (int i = curves - 2; i >= 0; --i)
            {
                if (!valid[i] && valid[i + 1])
                {
                    planes[i] = planes[i + 1];
                    valid[i] = true;
                }
            }
            SdfVector[] result = new SdfVector[curves + 1];
            for (int j = 0; j <= curves; ++j)
            {
                SdfVector sum = new SdfVector(0, 0, 0);
                if (j > 0)
                {
                    sum = sum + planes[j - 1];
                }
                if (j < curves)
                {
                    sum = sum + planes[j];
                }
                if (sum.LengthSquared() > 0)
                {
                    result[j] = sum.Normalized();
                }
                else
                {
                    int at = Math.Min(2 * j, points.Length - 2);
                    result[j] = RibbonBuilder.OrthogonalTo(points[at + 1] - points[at]);
                }
            }
            return result;
        }

        // The runs of a ribbon given in a cubic basis: every cubic curve as `pieces` quadratic ones. values: the points, the
        // widths and the normals (or null) at the control points of the basis; the colors (or null) are changed alike, into
        // the control values of a quadratic B-spline along the whole ribbon.
        public static List<Run> FromCubics(SdfVector[] points, double[] widths, SdfVector[] normals, double[,] basis, int step, int pieces, ref RGB[] colors)
        {
            double[] x = new double[points.Length], y = new double[points.Length], z = new double[points.Length];
            for (int i = 0; i < points.Length; ++i)
            {
                x[i] = points[i].x;
                y[i] = points[i].y;
                z[i] = points[i].z;
            }
            x = Rocaps.ToBezierCurves(x, basis, step);
            y = Rocaps.ToBezierCurves(y, basis, step);
            z = Rocaps.ToBezierCurves(z, basis, step);
            double[] w = Rocaps.ToBezierCurves(widths, basis, step);
            double[] nx = null, ny = null, nz = null;
            if (normals != null)
            {
                nx = new double[normals.Length];
                ny = new double[normals.Length];
                nz = new double[normals.Length];
                for (int i = 0; i < normals.Length; ++i)
                {
                    nx[i] = normals[i].x;
                    ny[i] = normals[i].y;
                    nz[i] = normals[i].z;
                }
                nx = Rocaps.ToBezierCurves(nx, basis, step);
                ny = Rocaps.ToBezierCurves(ny, basis, step);
                nz = Rocaps.ToBezierCurves(nz, basis, step);
            }
            int curves = x.Length / 4;
            double size = 0;
            for (int i = 0; i < x.Length; ++i)
            {
                size = Math.Max(size, Math.Max(Math.Abs(x[i] - x[0]), Math.Max(Math.Abs(y[i] - y[0]), Math.Abs(z[i] - z[0]))));
            }
            List<Run> runs = new List<Run>();
            int from = 0;
            for (int s = 1; s <= curves; ++s)
            {
                bool cut = s == curves;
                if (!cut)
                {
                    // The end of the curve before and the start of this one: apart, or with tangents that are not one
                    SdfVector end = new SdfVector(x[4 * s - 1], y[4 * s - 1], z[4 * s - 1]), start = new SdfVector(x[4 * s], y[4 * s], z[4 * s]);
                    SdfVector before = end - new SdfVector(x[4 * s - 2], y[4 * s - 2], z[4 * s - 2]);
                    SdfVector after = new SdfVector(x[4 * s + 1], y[4 * s + 1], z[4 * s + 1]) - start;
                    cut = (end - start).Length() > 1e-9 * size || SdfVector.Cross(before, after).Length() > 1e-3 * before.Length() * after.Length() ||
                          SdfVector.Dot(before, after) <= 0;
                }
                if (!cut)
                {
                    continue;
                }
                int count = s - from;
                double[] sx = SplineOfCubics(Slice(x, from, count), pieces, false), sy = SplineOfCubics(Slice(y, from, count), pieces, false);
                double[] sz = SplineOfCubics(Slice(z, from, count), pieces, false), sw = SplineOfCubics(Slice(w, from, count), pieces, true);
                SdfVector[] splinePoints = new SdfVector[sx.Length];
                for (int i = 0; i < sx.Length; ++i)
                {
                    splinePoints[i] = new SdfVector(sx[i], sy[i], sz[i]);
                    sw[i] = Math.Max(0, sw[i]);
                }
                SdfVector[] splineNormals = null;
                if (normals != null)
                {
                    double[] ax = SplineOfCubics(Slice(nx, from, count), pieces, true), ay = SplineOfCubics(Slice(ny, from, count), pieces, true);
                    double[] az = SplineOfCubics(Slice(nz, from, count), pieces, true);
                    splineNormals = new SdfVector[ax.Length];
                    for (int i = 0; i < ax.Length; ++i)
                    {
                        splineNormals[i] = new SdfVector(ax[i], ay[i], az[i]);
                    }
                }
                runs.Add(FromSpline(splinePoints, sw, splineNormals));
                from = s;
            }
            if (colors != null && colors.Length > 1)
            {
                double[] red = new double[colors.Length], green = new double[colors.Length], blue = new double[colors.Length];
                for (int i = 0; i < colors.Length; ++i)
                {
                    red[i] = colors[i].r;
                    green[i] = colors[i].g;
                    blue[i] = colors[i].b;
                }
                red = SplineOfCubics(Rocaps.ToBezierCurves(red, basis, step), pieces, true);
                green = SplineOfCubics(Rocaps.ToBezierCurves(green, basis, step), pieces, true);
                blue = SplineOfCubics(Rocaps.ToBezierCurves(blue, basis, step), pieces, true);
                colors = new RGB[red.Length];
                for (int i = 0; i < colors.Length; ++i)
                {
                    colors[i] = new RGB((Float)Math.Max(0, red[i]), (Float)Math.Max(0, green[i]), (Float)Math.Max(0, blue[i]));
                }
            }
            return runs;
        }

        // The four values of each of `count` cubic curves from the curve `from`
        private static double[] Slice(double[] values, int from, int count)
        {
            double[] part = new double[4 * count];
            Array.Copy(values, 4 * from, part, 0, 4 * count);
            return part;
        }

        public static pstd.vector<Shape> Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation, ParameterDictionary parameters,
                                                FileLoc loc)
        {
            List<Point3f> pointList = parameters.GetPoint3fArray("P");
            List<Normal3f> normalList = parameters.GetNormal3fArray("N");
            List<float> widthList = parameters.GetFloatArray("width");
            string basisName = parameters.GetOneString("basis", "b-spline");
            if (basisName == "bspline")
            {
                basisName = "b-spline";
            }
            if (!PatchBases.TryGet(basisName, out double[,] basis))
            {
                pbrt.ErrorExit(loc, "ribbon2: unknown basis \"" + basisName + "\" (b-spline, bezier, catmull-rom, hermite, power).");
            }
            bool cubicOnly = basisName != "b-spline" && basisName != "bezier";
            int degree = parameters.GetOneInt("degree", cubicOnly ? 3 : 2);
            if (degree != 2 && degree != 3 || (cubicOnly && degree != 3))
            {
                pbrt.ErrorExit(loc, "ribbon2: \"degree\" is 2 or 3, and 3 for the basis \"" + basisName + "\".");
            }
            int step = parameters.GetOneInt("step", PatchBases.DefaultStep(basisName));
            int pieces = parameters.GetOneInt("pieces", 4);
            if (degree == 3 && (step < 1 || pieces < 1 || pointList.Count < 4 || (pointList.Count - 4) % step != 0))
            {
                pbrt.ErrorExit(loc, "ribbon2: a cubic \"" + basisName + "\" needs 4 + n * step points \"P\" (step " + step + "), and \"pieces\" of at least 1.");
            }
            if (degree == 2 && (pointList.Count < 3 || (basisName == "bezier" && pointList.Count % 2 == 0)))
            {
                pbrt.ErrorExit(loc, basisName == "bezier" ? "ribbon2: a quadratic \"bezier\" needs 2n + 1 points \"P\"." : "ribbon2: at least three points \"P\" are needed.");
            }
            if (normalList.Count != 0 && normalList.Count != pointList.Count)
            {
                pbrt.ErrorExit(loc, "ribbon2: \"N\" needs a normal for each point.");
            }
            if (widthList.Count != 0 && widthList.Count != 1 && widthList.Count != pointList.Count)
            {
                pbrt.ErrorExit(loc, "ribbon2: \"width\" needs one value or one for each point.");
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
            List<RGB> colorList = parameters.GetRGBArray("Cs");
            if (colorList.Count > 1 && colorList.Count != points.Length)
            {
                pbrt.ErrorExit(loc, "ribbon2: \"Cs\" needs one color or one for each point.");
            }
            RGB[] colors = colorList.Count > 0 ? colorList.ToArray() : null;

            List<Run> runs = new List<Run>();
            if (degree == 3)
            {
                if (widthList.Count <= 1)
                {
                    // One width for all: not through the basis (the values of hermite and power are not all points)
                    double[] same = new double[points.Length];
                    for (int i = 0; i < same.Length; ++i)
                    {
                        same[i] = widths[0];
                    }
                    runs = FromCubics(points, same, normals, basis, step, pieces, ref colors);
                    foreach (Run run in runs)
                    {
                        for (int i = 0; i < run.widths.Length; ++i)
                        {
                            run.widths[i] = widths[0];
                        }
                    }
                }
                else
                {
                    runs = FromCubics(points, widths, normals, basis, step, pieces, ref colors);
                }
            }
            else if (basisName == "bezier")
            {
                Run run = new Run();
                run.points = points;
                run.widths = widths;
                if (normals != null)
                {
                    run.normals = new SdfVector[(points.Length + 1) / 2];
                    for (int j = 0; j < run.normals.Length; ++j)
                    {
                        run.normals[j] = normals[2 * j];
                    }
                }
                runs.Add(run);
                if (colors != null && colors.Length > 1)
                {
                    // The colors as the control values of a quadratic B-spline along the ribbon: the first, the middle one of
                    // each curve, the last (the colors given at the ends of the curves between are not kept)
                    int curves = (points.Length - 1) / 2;
                    RGB[] spline = new RGB[curves + 2];
                    spline[0] = colors[0];
                    spline[curves + 1] = colors[colors.Length - 1];
                    for (int i = 0; i < curves; ++i)
                    {
                        spline[i + 1] = colors[2 * i + 1];
                    }
                    colors = spline;
                }
            }
            else
            {
                runs.Add(FromSpline(points, widths, normals));
            }

            BezierSurfaceSettings settings = new BezierSurfaceSettings();
            settings.renderFromObject = renderFromObject;
            settings.objectFromRender = objectFromRender;
            settings.reverseOrientation = reverseOrientation;
            settings.transformSwapsHandedness = renderFromObject.SwapsHandedness();
            settings.epsilon = parameters.GetOneFloat("epsilon", 1e-5f) * diagonal;
            bool solidAngle = parameters.GetOneBool("solidanglesampling", true);
            int strandColors = colors != null ? StrandColors.Register(colors, false) : 0;
            pstd.vector<Shape> shapes = new pstd.vector<Shape>();
            foreach (RibbonSegment segment in Build(runs))
            {
                shapes.push_back(new Ribbon2Shape(settings, segment, strandColors, solidAngle));
            }
            return shapes;
        }
    }
}
