// (not in pbrt-v4) ribbon2shape.cs
//
// The "ribbon2" shape: the curved ribbons of Reshetov (ribbon.cs) with more ways to give the centerline and with better sampling
// as a light. The segments and their ray intersection are those of "ribbon" (RibbonSegment); "ribbon" itself is as it was.
//
//   Shape "ribbon2" "point3 P" [...] ("string basis" "b-spline") ("integer degree" 2) ("integer step" ...) ("string wrap" "nonperiodic")
//         ("float tension" 0.5) ("float knots" [...]) ("integer pieces" 4)
//         ("normal N" [...]) ("float width" [...]) ("rgb Cs" [...]) ("float epsilon" 1e-5) ("bool solidanglesampling" true)
//         ("bool flipturns" false)
//
//   flipturns: with normals, the behavior of "ribbon" where a curve turns by more than a right angle (Ribbons2.Build): the ribbon
//   turns its sides there and shows its other face from there on, instead of lying across the normals as given. With the
//   default basis the segments are those of "ribbon".
//   flippieces (cubic bases, with flipturns; "pieces" by default): the pieces to a cubic curve that the rule of flipturns is
//   applied to. "ribbon" turns its sides because its segments are long, and the pieces of a cubic curve seldom turn by a right
//   angle; with fewer "flippieces" (1: the whole curve) the ribbon turns its sides as "ribbon" would over curves that long,
//   while its centerline stays that of "pieces" (Ribbons2.TakeBitangents).
//   flipstretch (cubic bases, with flipturns and normals): "curve" (the default: the rule over the pieces above) or "bend":
//   the rule over stretches from the middle of one cubic curve to the middle of the next, which go round the bends of the
//   strand as the segments of "ribbon" do, so that the ribbon turns its sides at the bends as "ribbon" does over its own
//   basis (Ribbons2.BendStretches).
//
//   The basis of the centerline is one of strandbasis.cs.
//     "b-spline" with the degree 2 (the default, the quadratic B-spline of "ribbon": the same segments), "bezier" with the degree 2
//       (2n + 1 points: the ends and the middle point of each curve), "linear", and the "b-spline" with "knots" and the degree 2:
//       these are quadratic curves, the centerline of the paper itself, and nothing is approximated. (Straight pieces and corners
//       of repeated knots meet at an angle; the ribbon turns there without a smooth joint.)
//     "bezier", "b-spline" with the degree 3 (with or without knots), "catmull-rom", "cardinal", "hermite", "power": the centerline
//       of the paper is quadratic, so that the ray intersection is a cubic equation, and a cubic curve is not a quadratic one.
//       Each cubic curve is replaced by "pieces" quadratic curves: the curve is cut at
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
        // start, extent: the part of the parameter of the whole ribbon that the segment covers (the texture coordinate u)
        public Ribbon2Shape(BezierSurfaceSettings settings, RibbonSegment segment, double start, double extent, int strandColors, bool solidAngleSampling)
        {
            this.start = start;
            this.extent = extent;
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
            double u = Math.Max(0, Math.Min(1, ((double)intr.uv.x - start) / extent));
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
            dpdu = renderFromObject.Apply(new Vector3f((Float)(du.x / extent), (Float)(du.y / extent), (Float)(du.z / extent)));
            dpdv = renderFromObject.Apply(new Vector3f((Float)(dv.x * 2), (Float)(dv.y * 2), (Float)(dv.z * 2)));
            uv = new Point2f((Float)(start + u * extent), (Float)(0.5 * (v + 1)));
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
        private readonly double start;
        private readonly double extent;
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

        // One quadratic curve of a ribbon: its three points and half widths, the normals at its ends, and the part of the
        // parameter of the whole ribbon that it covers
        private sealed class Piece
        {
            public SdfVector p0, p1, p2;
            public double w0, w1, w2;
            public SdfVector n0, n1;
            public double start, extent;
        }

        // The segments of the runs of one ribbon, numbered along the whole ribbon, with the part of the parameter of the ribbon
        // (0 at its start, 1 at its end, the same step for every curve given) that each segment covers. Within a run the
        // bitangent is carried from one curve to the next, and where two curves meet with one tangent the surface normals are
        // made the same along the joint (Section 4.1 of the paper).
        //
        // With normals the ribbon is to lie across them: its bitangent at u is to be along T(u) x n(u), n going round from the
        // normal of one end of a curve to that of the other. The bitangent of a segment goes in a line from that of its start
        // to that of its end (with the terms of the joint added), which is not that turn when the two are far apart: the ribbon
        // then leans, and over a large turn folds. So the segments are built, the angle of each from T x n is measured at three
        // places, the curves that are off by more than Tolerance are cut in two at the middle of their parameter (the same
        // centerline and widths), and the segments are built again, until none is off (at most 6 times). Only the curves that
        // need it are cut, and as far as they need it.
        //
        // flip (the behavior of "ribbon" with normals): no curve is cut; where the bitangent at the end of a curve is more
        // than a right angle from that at its start it is turned to its other side, so that the ribbon turns its sides there
        // and shows its other face from there on. For the quadratic B-spline the segments are those of "ribbon".
        public static List<RibbonSegment> Build(List<Run> runs, bool flip, out double[] starts, out double[] extents)
        {
            int total = 0;
            foreach (Run run in runs)
            {
                total += (run.points.Length - 1) / 2;
            }
            List<List<Piece>> pieces = new List<List<Piece>>();
            int curve = 0;
            foreach (Run run in runs)
            {
                int given = (run.points.Length - 1) / 2;
                SdfVector[] normals = run.normals ?? PlaneNormals(run.points, given);
                List<Piece> list = new List<Piece>();
                for (int i = 0; i < given; ++i)
                {
                    Piece piece = new Piece();
                    piece.p0 = run.points[2 * i];
                    piece.p1 = run.points[2 * i + 1];
                    piece.p2 = run.points[2 * i + 2];
                    piece.w0 = run.widths[2 * i];
                    piece.w1 = run.widths[2 * i + 1];
                    piece.w2 = run.widths[2 * i + 2];
                    piece.n0 = normals[i];
                    piece.n1 = normals[i + 1];
                    piece.start = (double)(curve + i) / total;
                    piece.extent = 1.0 / total;
                    list.Add(piece);
                }
                curve += given;
                pieces.Add(list);
            }
            List<RibbonSegment> segments = null;
            for (int pass = 0; pass <= 6; ++pass)
            {
                segments = Assemble(runs, pieces, flip);
                if (pass == 6 || flip)
                {
                    break;
                }
                bool cut = false;
                int at = 0;
                for (int r = 0; r < runs.Count; ++r)
                {
                    List<Piece> list = pieces[r];
                    if (runs[r].normals == null)
                    {
                        at += list.Count;
                        continue;
                    }
                    List<Piece> next = new List<Piece>();
                    foreach (Piece piece in list)
                    {
                        if (Lean(segments[at++], piece) < 1)
                        {
                            Halve(piece, next);
                            cut = true;
                        }
                        else
                        {
                            next.Add(piece);
                        }
                    }
                    pieces[r] = next;
                }
                if (!cut)
                {
                    break;
                }
            }
            starts = new double[segments.Count];
            extents = new double[segments.Count];
            int index = 0;
            foreach (List<Piece> list in pieces)
            {
                foreach (Piece piece in list)
                {
                    starts[index] = piece.start;
                    extents[index++] = piece.extent;
                }
            }
            return segments;
        }

        // The cosines of the angles that the bitangent of a segment of a ribbon with normals may be off by: 2 degrees round
        // the centerline (the ribbon is turned out of its plane by as much), and 15 degrees along it (the ribbon stays in
        // its plane, and is narrower by the cosine: 3 percent)
        private const double Tolerance = 0.9993908270190958;
        private const double ToleranceAlong = 0.9659258262890683;

        // How far the bitangent of the segment is from T x n of its curve at a quarter, a half and three quarters of the
        // parameter, against the tolerances: the least of the cosine of its angle round the centerline over Tolerance and the
        // cosine of its angle along the centerline over ToleranceAlong (less than 1: the curve is to be cut)
        private static double Lean(RibbonSegment segment, Piece piece)
        {
            SdfVector n0 = piece.n0.Normalized(), n1 = piece.n1.Normalized();
            double cosine = Math.Max(-1, Math.Min(1, SdfVector.Dot(n0, n1))), angle = Math.Acos(cosine), sine = Math.Sin(angle);
            double least = double.MaxValue;
            for (int i = 1; i <= 3; ++i)
            {
                double u = 0.25 * i;
                // (the normal round from one end to the other; in a line where the two are the same or opposite)
                SdfVector normal = sine > 1e-6 ? n0 * (Math.Sin((1 - u) * angle) / sine) + n1 * (Math.Sin(u * angle) / sine) : n0 * (1 - u) + n1 * u;
                SdfVector tangent = segment.Velocity(u);
                SdfVector across = SdfVector.Cross(tangent, normal);
                SdfVector bitangent = segment.Position(u, 1) - segment.Position(u, -1);
                double tangentSquared = tangent.LengthSquared(), length = bitangent.Length();
                if (!(tangentSquared > 0) || !(length > 0) || !(across.LengthSquared() > 0))
                {
                    continue;
                }
                // The bitangent without its part along the centerline
                SdfVector upright = bitangent - tangent * (SdfVector.Dot(bitangent, tangent) / tangentSquared);
                double uprightLength = upright.Length();
                double round = uprightLength > 0 ? SdfVector.Dot(upright, across) / (uprightLength * across.Length()) : -1;
                least = Math.Min(least, Math.Min(round / Tolerance, uprightLength / length / ToleranceAlong));
            }
            return least;
        }

        // The two halves of the curve, added to the list: the same centerline and widths; the normal at the cut is the one half
        // way round from one end to the other
        private static void Halve(Piece piece, List<Piece> list)
        {
            SdfVector middle = piece.n0.Normalized() + piece.n1.Normalized();
            if (!(middle.LengthSquared() > 0))
            {
                middle = piece.n0;
            }
            SdfVector a = (piece.p0 + piece.p1) * 0.5, b = (piece.p1 + piece.p2) * 0.5, m = (a + b) * 0.5;
            double wa = 0.5 * (piece.w0 + piece.w1), wb = 0.5 * (piece.w1 + piece.w2), wm = 0.5 * (wa + wb);
            Piece first = new Piece(), second = new Piece();
            first.p0 = piece.p0;
            first.p1 = a;
            first.p2 = m;
            first.w0 = piece.w0;
            first.w1 = wa;
            first.w2 = wm;
            first.n0 = piece.n0;
            first.n1 = middle;
            first.start = piece.start;
            first.extent = 0.5 * piece.extent;
            second.p0 = m;
            second.p1 = b;
            second.p2 = piece.p2;
            second.w0 = wm;
            second.w1 = wb;
            second.w2 = piece.w2;
            second.n0 = middle;
            second.n1 = piece.n1;
            second.start = piece.start + 0.5 * piece.extent;
            second.extent = 0.5 * piece.extent;
            list.Add(first);
            list.Add(second);
        }

        // The segments of the curves of the runs
        private static List<RibbonSegment> Assemble(List<Run> runs, List<List<Piece>> pieces, bool flip)
        {
            List<RibbonSegment> segments = new List<RibbonSegment>();
            for (int r = 0; r < runs.Count; ++r)
            {
                List<Piece> list = pieces[r];
                int first = segments.Count;
                for (int i = 0; i < list.Count; ++i)
                {
                    Piece piece = list[i];
                    RibbonSegment segment = new RibbonSegment();
                    segment.index = first + i;
                    segment.count = 0;
                    segment.b0 = piece.p0;
                    segment.b1 = piece.p1;
                    segment.b2 = piece.p2;
                    segment.width0 = piece.w0;
                    segment.width1 = piece.w1;
                    segment.width2 = piece.w2;
                    SdfVector leaving = segment.b1 - segment.b0, arriving = segment.b2 - segment.b1;
                    SdfVector start = Bitangent(leaving, piece.n0), end = Bitangent(arriving, piece.n1);
                    bool smooth = false;
                    double ratio = 1;
                    if (i > 0)
                    {
                        // The bitangent of the end of the curve before, unless the centerline turns at the joint
                        RibbonSegment previous = segments[first + i - 1];
                        SdfVector before = previous.b2 - previous.b1;
                        double turn = SdfVector.Cross(before, leaving).Length(), scale = before.Length() * leaving.Length();
                        if (turn <= 1e-3 * scale && SdfVector.Dot(before, leaving) > 0)
                        {
                            start = previous.bitangent1.Normalized();
                            smooth = true;
                            // (1 where the derivatives are the same, as those of a uniform B-spline)
                            ratio = (before - leaving).Length() <= 1e-6 * Math.Sqrt(scale) ? 1 : leaving.Length() / before.Length();
                        }
                    }
                    // Without normals the bitangent of the end is turned to the side of that of the start (as "ribbon" does).
                    // With normals it is left as the normals give it ("ribbon" turns it there too, which swaps the sides of
                    // the ribbon from there on).
                    if ((flip || runs[r].normals == null) && SdfVector.Dot(start, end) < 0)
                    {
                        end = -end;
                    }
                    segment.bitangent0 = start;
                    segment.bitangent1 = end;
                    segment.beta = 0;
                    segment.gamma = 0;
                    if (smooth)
                    {
                        Connect(segments[first + i - 1], segment, ratio);
                    }
                    segments.Add(segment);
                }
            }
            foreach (RibbonSegment segment in segments)
            {
                segment.count = segments.Count;
            }
            return segments;
        }

        // RibbonBuilder.Connect (Section 4.1 of the paper) for two segments whose derivatives at the joint have one direction but
        // not one length (the halves of a cut curve beside a whole one, a B-spline with knots): the terms beta, gamma of the
        // segment next that make the surface normals of both segments the same along the joint. ratio: the length of the
        // derivative of next at the joint over that of previous; the derivative of the generator of previous, taken in the
        // parameter of next, is that many times as long.
        private static void Connect(RibbonSegment previous, RibbonSegment next, double ratio)
        {
            SdfVector b = next.bitangent0;
            SdfVector c1 = 2 * (previous.b1 - previous.b0), c2 = previous.b0 - 2 * previous.b1 + previous.b2;
            double previousAlpha = previous.bitangent1.Length();
            // g'(1) of the previous segment
            SdfVector previousGenerator = previous.bitangent1 - previous.bitangent0 - (previous.beta * c2 + previous.gamma * (c1 + c2));
            SdfVector nc1 = 2 * (next.b1 - next.b0), nc2 = next.b0 - 2 * next.b1 + next.b2;
            SdfVector unitEnd = next.bitangent1.Normalized();
            // A basis of the plane perpendicular to b
            SdfVector e1 = RibbonBuilder.OrthogonalTo(b);
            SdfVector e2 = SdfVector.Cross(b, e1).Normalized();
            double m11 = SdfVector.Dot(e1, nc2), m12 = SdfVector.Dot(e1, nc1);
            double m21 = SdfVector.Dot(e2, nc2), m22 = SdfVector.Dot(e2, nc1);
            double determinant = m11 * m22 - m12 * m21;
            if (!(previousAlpha > 0) || Math.Abs(determinant) < 1e-12 * Math.Max(1e-300, Math.Abs(m11 * m22) + Math.Abs(m12 * m21)))
            {
                next.bitangent1 = unitEnd;
                return;
            }
            SdfVector target = previousGenerator / previousAlpha;
            if (ratio != 1)
            {
                target = target * ratio;
            }
            double r1 = SdfVector.Dot(e1, target), r2 = SdfVector.Dot(e2, target);
            double s1 = SdfVector.Dot(e1, unitEnd), s2 = SdfVector.Dot(e2, unitEnd);
            // [beta, gamma] = M^-1 (r - alpha s) = p + alpha q
            double p0 = (m22 * r1 - m12 * r2) / determinant, p1 = (-m21 * r1 + m11 * r2) / determinant;
            double q0 = -(m22 * s1 - m12 * s2) / determinant, q1 = -(-m21 * s1 + m11 * s2) / determinant;
            double denominator = q0 * q0 + q1 * q1;
            double alpha = denominator > 0 ? -(p0 * q0 + p1 * q1) / denominator : 1;
            if (!(alpha > 1e-3) || double.IsNaN(alpha) || double.IsInfinity(alpha))
            {
                // the bitangent must keep its direction: take the smallest positive length that is allowed
                alpha = 1;
            }
            next.beta = p0 + alpha * q0;
            next.gamma = p1 + alpha * q1;
            next.bitangent1 = unitEnd * alpha;
            if (double.IsNaN(next.beta) || double.IsNaN(next.gamma) || double.IsInfinity(next.beta) || double.IsInfinity(next.gamma))
            {
                next.beta = 0;
                next.gamma = 0;
                next.bitangent1 = unitEnd;
            }
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
            // A straight curve (all those of the basis "linear"): the plane of the turn to the curve after it, or before it
            for (int i = 0; i < curves; ++i)
            {
                if (valid[i])
                {
                    continue;
                }
                SdfVector along = points[2 * i + 2] - points[2 * i];
                for (int side = 1; side >= -1 && planes[i].LengthSquared() == 0; side -= 2)
                {
                    int other = i + side;
                    if (other < 0 || other >= curves)
                    {
                        continue;
                    }
                    SdfVector next = points[2 * other + 2] - points[2 * other];
                    SdfVector normal = side > 0 ? SdfVector.Cross(along, next) : SdfVector.Cross(next, along);
                    if (normal.LengthSquared() > 1e-24 * along.LengthSquared() * next.LengthSquared())
                    {
                        planes[i] = normal.Normalized();
                    }
                }
            }
            for (int i = 0; i < curves; ++i)
            {
                valid[i] = valid[i] || planes[i].LengthSquared() > 0;
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

        // The runs of a ribbon of quadratic Bezier curves, three values for each curve (the coordinates of the points, the half
        // widths, and the coordinates of the normals or null): the curves that follow each other without a gap are one run
        public static List<Run> FromQuadratics(double[] x, double[] y, double[] z, double[] w, double[] nx, double[] ny, double[] nz)
        {
            int curves = x.Length / 3;
            double size = Size(x, y, z);
            List<Run> runs = new List<Run>();
            int from = 0;
            for (int s = 1; s <= curves; ++s)
            {
                if (s < curves && Apart(x, y, z, 3 * s - 1, 3 * s) <= 1e-9 * size)
                {
                    continue;
                }
                int count = s - from;
                Run run = new Run();
                run.points = new SdfVector[2 * count + 1];
                run.widths = new double[2 * count + 1];
                run.normals = nx == null ? null : new SdfVector[count + 1];
                for (int i = 0; i < count; ++i)
                {
                    int at = 3 * (from + i);
                    for (int k = 0; k < 3; ++k)
                    {
                        run.points[2 * i + k] = new SdfVector(x[at + k], y[at + k], z[at + k]);
                        run.widths[2 * i + k] = Math.Max(0, w[at + k]);
                    }
                    if (nx != null)
                    {
                        run.normals[i] = new SdfVector(nx[at], ny[at], nz[at]);
                        run.normals[i + 1] = new SdfVector(nx[at + 2], ny[at + 2], nz[at + 2]);
                    }
                }
                runs.Add(run);
                from = s;
            }
            return runs;
        }

        // The runs of a ribbon of cubic Bezier curves, four values for each curve: every cubic curve as `pieces` quadratic ones.
        // The ribbon is cut where two curves do not meet, or meet at an angle.
        public static List<Run> FromCubics(double[] x, double[] y, double[] z, double[] w, double[] nx, double[] ny, double[] nz, int pieces)
        {
            int curves = x.Length / 4;
            double size = Size(x, y, z);
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
                if (nx != null)
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
            return runs;
        }

        // The four values of each of `count` cubic curves from the curve `from`
        private static double[] Slice(double[] values, int from, int count)
        {
            double[] part = new double[4 * count];
            Array.Copy(values, 4 * from, part, 0, 4 * count);
            return part;
        }

        private static double Size(double[] x, double[] y, double[] z)
        {
            double size = 0;
            for (int i = 0; i < x.Length; ++i)
            {
                size = Math.Max(size, Math.Max(Math.Abs(x[i] - x[0]), Math.Max(Math.Abs(y[i] - y[0]), Math.Abs(z[i] - z[0]))));
            }
            return size;
        }

        private static double Apart(double[] x, double[] y, double[] z, int a, int b)
        {
            return new SdfVector(x[a] - x[b], y[a] - y[b], z[a] - z[b]).Length();
        }

        // The control values of the quadratic B-spline of the colors along a ribbon of quadratic curves (three values each):
        // the first value, the middle one of each curve, the last
        private static double[] SplineOfQuadratics(double[] values)
        {
            int curves = values.Length / 3;
            double[] spline = new double[curves + 2];
            spline[0] = values[0];
            spline[curves + 1] = values[values.Length - 1];
            for (int s = 0; s < curves; ++s)
            {
                spline[s + 1] = values[3 * s + 1];
            }
            return spline;
        }

        // The bitangents of the segments of a strand of cubic curves (`pieces` to a curve) taken from the segments `coarse` of the
        // same strand (`coarsePieces` to a curve): the bitangent of a segment goes in a line between those of `coarse` at its
        // two ends, without the terms of the joints. The centerline is that of the many pieces, and the ribbon turns its sides
        // where the few do ("flippieces").
        public static void TakeBitangents(List<RibbonSegment> segments, int pieces, List<RibbonSegment> coarse, int coarsePieces)
        {
            for (int i = 0; i < segments.Count; ++i)
            {
                int curve = i / pieces;
                // The part of the parameter of the curve of the segment, and the coarse segment over its middle
                double from = (double)(i % pieces) / pieces, to = (double)(i % pieces + 1) / pieces;
                int part = Math.Min(coarsePieces - 1, (int)(0.5 * (from + to) * coarsePieces));
                RibbonSegment source = coarse[curve * coarsePieces + part];
                segments[i].bitangent0 = source.Generator(from * coarsePieces - part);
                segments[i].bitangent1 = source.Generator(to * coarsePieces - part);
                segments[i].beta = 0;
                segments[i].gamma = 0;
            }
        }

        // The bitangents of a strand of cubic Bezier curves with normals (four values for each curve) by the rule of "ribbon"
        // over stretches that go round the bends: "ribbon" turns the sides of the ribbon where the bitangent at the end of a
        // segment is more than a right angle from that at its start, and its segments run from the middle of one side of the
        // control polygon to the middle of the next, round a bend of the strand. The curves of the cubic bases run from one bend
        // to the next, and their ends have nearly one direction. So here the stretches run from the middle of one curve to the
        // middle of the next (and over the first half of the first curve and the last half of the last one of a run of curves
        // that follow each other): at the ends of each the bitangent is T x n, turned to the side of the one before when it is
        // more than a right angle from it, and in between it goes in a line. Returned as two segments for each curve (its two
        // halves) of which only the bitangents are set, for TakeBitangents and for "ribbon3".
        public static List<RibbonSegment> BendStretches(double[] x, double[] y, double[] z, double[] nx, double[] ny, double[] nz)
        {
            int curves = x.Length / 4;
            double size = Size(x, y, z);
            List<RibbonSegment> halves = new List<RibbonSegment>();
            SdfVector before = new SdfVector(0, 0, 0);
            SdfVector middleBefore = new SdfVector(0, 0, 0);
            for (int s = 0; s < curves; ++s)
            {
                bool first = s == 0 || Apart(x, y, z, 4 * s - 1, 4 * s) > 1e-9 * size;
                bool last = s == curves - 1 || Apart(x, y, z, 4 * s + 3, 4 * s + 4) > 1e-9 * size;
                // The bitangent at the middle of the curve, on the side of the end of the stretch before
                SdfVector middle = Across(x, y, z, nx, ny, nz, s, 0.5);
                if (first)
                {
                    before = Across(x, y, z, nx, ny, nz, s, 0);
                }
                else
                {
                    before = middleBefore;
                }
                if (SdfVector.Dot(before, middle) < 0)
                {
                    middle = -middle;
                }
                // The first half: the end of the stretch from the middle of the curve before (its second half), or the whole
                // stretch from the start of the run
                RibbonSegment firstHalf = new RibbonSegment();
                firstHalf.bitangent0 = first ? before : (before + middle) * 0.5;
                firstHalf.bitangent1 = middle;
                halves.Add(firstHalf);
                // The second half: the start of the stretch to the middle of the curve after, or the whole stretch to the end
                // of the run
                SdfVector after;
                if (last)
                {
                    after = Across(x, y, z, nx, ny, nz, s, 1);
                }
                else
                {
                    after = Across(x, y, z, nx, ny, nz, s + 1, 0.5);
                }
                if (SdfVector.Dot(middle, after) < 0)
                {
                    after = -after;
                }
                RibbonSegment secondHalf = new RibbonSegment();
                secondHalf.bitangent0 = middle;
                secondHalf.bitangent1 = last ? after : (middle + after) * 0.5;
                halves.Add(secondHalf);
                middleBefore = middle;
            }
            return halves;
        }

        // T x n of unit length at u of the cubic curve
        private static SdfVector Across(double[] x, double[] y, double[] z, double[] nx, double[] ny, double[] nz, int curve, double u)
        {
            SdfVector tangent = new SdfVector(CubicDerivative(x, curve, u), CubicDerivative(y, curve, u), CubicDerivative(z, curve, u));
            SdfVector normal = new SdfVector(Cubic(nx, curve, u), Cubic(ny, curve, u), Cubic(nz, curve, u));
            return Bitangent(tangent, normal);
        }

        // The runs of a strand in a basis: the values at the control points (the coordinates of the points; the half widths, or
        // null and one `width` for all; the coordinates of the normals, or null). The quadratic curves of the ribbon itself for
        // the bases of the degrees 1 and 2; the cubic curves in `pieces` quadratic ones otherwise.
        public static List<Run> Runs(StrandBasis basis, double[] x, double[] y, double[] z, double[] widths, double width, double[] nx, double[] ny, double[] nz, int pieces)
        {
            int degree = basis.Degree == 3 ? 3 : 2;
            x = basis.Vectors(x, degree);
            y = basis.Vectors(y, degree);
            z = basis.Vectors(z, degree);
            double[] w = new double[x.Length];
            if (widths != null)
            {
                w = basis.Curves(widths, degree);
            }
            else
            {
                // One width for all: not through the basis (the values of hermite and power are not all points)
                for (int i = 0; i < w.Length; ++i)
                {
                    w[i] = width;
                }
            }
            if (nx != null)
            {
                nx = basis.Vectors(nx, degree);
                ny = basis.Vectors(ny, degree);
                nz = basis.Vectors(nz, degree);
            }
            return degree == 3 ? FromCubics(x, y, z, w, nx, ny, nz, pieces) : FromQuadratics(x, y, z, w, nx, ny, nz);
        }

        public static pstd.vector<Shape> Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation, ParameterDictionary parameters,
                                                FileLoc loc)
        {
            List<Point3f> pointList = parameters.GetPoint3fArray("P");
            List<Normal3f> normalList = parameters.GetNormal3fArray("N");
            List<float> widthList = parameters.GetFloatArray("width");
            StrandBasis basis = StrandBasis.Read(parameters, "ribbon2", "b-spline", 2, loc);
            string problem = basis.Check(pointList.Count);
            if (problem != null)
            {
                pbrt.ErrorExit(loc, "ribbon2: \"P\": " + problem + ".");
            }
            int pieces = parameters.GetOneInt("pieces", 4);
            if (pieces < 1)
            {
                pbrt.ErrorExit(loc, "ribbon2: \"pieces\" is at least 1.");
            }
            if (normalList.Count != 0 && normalList.Count != pointList.Count)
            {
                pbrt.ErrorExit(loc, "ribbon2: \"N\" needs a normal for each point.");
            }
            if (widthList.Count != 0 && widthList.Count != 1 && widthList.Count != pointList.Count)
            {
                pbrt.ErrorExit(loc, "ribbon2: \"width\" needs one value or one for each point.");
            }
            List<RGB> colorList = parameters.GetRGBArray("Cs");
            if (colorList.Count > 1 && colorList.Count != pointList.Count)
            {
                pbrt.ErrorExit(loc, "ribbon2: \"Cs\" needs one color or one for each point.");
            }
            int count = pointList.Count;
            double[] x = new double[count], y = new double[count], z = new double[count];
            SdfVector minimum = new SdfVector(double.MaxValue, double.MaxValue, double.MaxValue);
            SdfVector maximum = new SdfVector(-double.MaxValue, -double.MaxValue, -double.MaxValue);
            for (int i = 0; i < count; ++i)
            {
                x[i] = pointList[i].x;
                y[i] = pointList[i].y;
                z[i] = pointList[i].z;
                minimum = new SdfVector(Math.Min(minimum.x, x[i]), Math.Min(minimum.y, y[i]), Math.Min(minimum.z, z[i]));
                maximum = new SdfVector(Math.Max(maximum.x, x[i]), Math.Max(maximum.y, y[i]), Math.Max(maximum.z, z[i]));
            }
            double diagonal = (maximum - minimum).Length();
            int degree = basis.Degree == 3 ? 3 : 2;
            double[] givenWidths = null;
            if (widthList.Count > 1)
            {
                givenWidths = new double[count];
                for (int i = 0; i < count; ++i)
                {
                    givenWidths[i] = widthList[i];
                }
            }
            double[] nx = null, ny = null, nz = null;
            if (normalList.Count != 0)
            {
                nx = new double[count];
                ny = new double[count];
                nz = new double[count];
                for (int i = 0; i < count; ++i)
                {
                    nx[i] = normalList[i].x;
                    ny[i] = normalList[i].y;
                    nz[i] = normalList[i].z;
                }
            }
            List<Run> runs = Runs(basis, x, y, z, givenWidths, widthList.Count == 1 ? widthList[0] : 0.05 * diagonal, nx, ny, nz, pieces);
            RGB[] colors = colorList.Count > 0 ? colorList.ToArray() : null;
            if (colors != null && colors.Length > 1)
            {
                // The colors along the whole ribbon, as the control values of a quadratic B-spline with a curve for each segment
                double[] red = new double[count], green = new double[count], blue = new double[count];
                for (int i = 0; i < count; ++i)
                {
                    red[i] = colors[i].r;
                    green[i] = colors[i].g;
                    blue[i] = colors[i].b;
                }
                red = degree == 3 ? SplineOfCubics(basis.Curves(red, 3), pieces, true) : SplineOfQuadratics(basis.Curves(red, 2));
                green = degree == 3 ? SplineOfCubics(basis.Curves(green, 3), pieces, true) : SplineOfQuadratics(basis.Curves(green, 2));
                blue = degree == 3 ? SplineOfCubics(basis.Curves(blue, 3), pieces, true) : SplineOfQuadratics(basis.Curves(blue, 2));
                colors = new RGB[red.Length];
                for (int i = 0; i < colors.Length; ++i)
                {
                    colors[i] = new RGB((Float)Math.Max(0, red[i]), (Float)Math.Max(0, green[i]), (Float)Math.Max(0, blue[i]));
                }
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
            bool flip = parameters.GetOneBool("flipturns", false);
            int flipPieces = parameters.GetOneInt("flippieces", pieces);
            if (flipPieces < 1)
            {
                pbrt.ErrorExit(loc, "ribbon2: \"flippieces\" is at least 1.");
            }
            List<RibbonSegment> segments = Build(runs, flip, out double[] starts, out double[] extents);
            string flipStretch = parameters.GetOneString("flipstretch", "curve");
            if (flipStretch != "curve" && flipStretch != "bend")
            {
                pbrt.ErrorExit(loc, "ribbon2: \"flipstretch\" is \"curve\" or \"bend\".");
            }
            if (flip && flipStretch == "bend")
            {
                if (degree != 3 || nx == null || pieces % 2 != 0)
                {
                    pbrt.ErrorExit(loc, "ribbon2: \"flipstretch\" \"bend\" is for the cubic bases, with \"N\" and an even number of \"pieces\".");
                }
                TakeBitangents(segments, pieces, BendStretches(basis.Vectors(x, 3), basis.Vectors(y, 3), basis.Vectors(z, 3), basis.Vectors(nx, 3), basis.Vectors(ny, 3),
                                                                basis.Vectors(nz, 3)), 2);
            }
            else if (flip && degree == 3 && flipPieces != pieces)
            {
                List<RibbonSegment> coarse = Build(Runs(basis, x, y, z, givenWidths, widthList.Count == 1 ? widthList[0] : 0.05 * diagonal, nx, ny, nz, flipPieces), true,
                                                   out double[] coarseStarts, out double[] coarseExtents);
                TakeBitangents(segments, pieces, coarse, flipPieces);
            }
            for (int i = 0; i < segments.Count; ++i)
            {
                shapes.push_back(new Ribbon2Shape(settings, segments[i], starts[i], extents[i], strandColors, solidAngle));
            }
            return shapes;
        }
    }
}
