// (not in pbrt-v4) rocapshape.cs
//
// The shape of one segment of a hair strand (rocap.cs), and the "rocap" shape of a scene file.

using System;
using System.Collections.Generic;

namespace pbrt
{
    public sealed unsafe class RocapShape : Shape
    {
        // Whether the samples for a reference point are spread close to the solid angle of the strand (see Shares) instead of by
        // the area of the side that faces the point
        public bool SolidAngleSampling = true;

        public RocapShape(BezierSurfaceSettings settings, RocapSegment segment, int strandColors = 0)
        {
            this.strandColors = strandColors;
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

        // The area of the tube (the envelope of the spheres) and of the caps at the ends of the strand
        public override Float Area()
        {
            return (Float)Table().area;
        }

        // The surface that is sampled is the tube and the caps at the two ends of the strand (the first and the last segment of
        // a chain; the ends of the segments between are inside their neighbours). One of the three is chosen in proportion to
        // its area. The tube: a bin of the parameter u by the mass 2 pi r slant, then the angle around the strand uniformly. A
        // round cap (the part of the sphere at the end beyond the circle where the tube leaves it): uniformly. A flat cap (the
        // cut of the solid by the plane of the end): uniformly in a disk of the plane that holds the cut, and no sample for a
        // point of the disk that is not on the surface, which leaves the density of the points that are as it is.
        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            return SampleSurface(u, false, new SdfVector(0, 0, 0));
        }

        // The density of Sample(u) at a point of the surface, by area
        public override Float PDF(Interaction intr)
        {
            SamplingTable table = Table();
            Point3f po = settings.objectFromRender.Apply(intr.p());
            SdfVector point = new SdfVector(po.x, po.y, po.z);
            double parameter = Math.Max(0, Math.Min(1, (double)intr.uv.x * segment.count - segment.index));
            Normal3f no = settings.objectFromRender.Apply(intr.n);
            SdfVector normalObject = new SdfVector(no.x, no.y, no.z).Normalized();
            for (int end = 0; end < 2; ++end)
            {
                if (OnCap(end, parameter, point, normalObject, table, 0))
                {
                    return (Float)CapDensity(end, table, CapNormal(end, point));
                }
            }
            // The tube: the angle around the strand of the point
            if (!InsideSlab(point))
            {
                return 0;
            }
            SdfVector radial = point - segment.Position(parameter);
            SdfVector e1 = Frame(parameter, out SdfVector e2);
            double angle = Math.Atan2(SdfVector.Dot(radial, e2), SdfVector.Dot(radial, e1));
            if (!Surface(parameter, angle, out Point3f p, out Normal3f n, out double jacobian) || !(jacobian > 0))
            {
                return 0;
            }
            double pdf = table.tubeShare * AreaDensity(table, parameter) / (2 * Math.PI) / jacobian;
            return double.IsInfinity(pdf) || double.IsNaN(pdf) ? 0 : (Float)pdf;
        }

        // From a reference point only the side of the tube that faces the point is sampled: at the parameter u the normals
        // n = s t + c (e1 cos + e2 sin) that see the point D = reference - q(u) are those with n . D > r, an arc of the angle
        // about the direction of D across the strand
        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            Point3f reference = settings.objectFromRender.Apply(ctx.p());
            return PatchSampling.ToSolidAngle(SampleSurface(u, true, new SdfVector(reference.x, reference.y, reference.z)), ctx);
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            Ray ray = ctx.SpawnRay(wi);
            if (!Trace(ray, Infinity, out double t, out double u, out SdfVector normal, out SdfVector hitPoint, out double length))
            {
                return 0;
            }
            SamplingTable table = Table();
            Point3f referencePoint = settings.objectFromRender.Apply(ctx.p());
            SdfVector toward = new SdfVector(referencePoint.x, referencePoint.y, referencePoint.z);
            Span<double> weights = stackalloc double[Nodes + 1];
            Shares(toward, table, weights, out double total, out double tubeShare, out double startShare, out double endShare);
            for (int end = 0; end < 2; ++end)
            {
                if (!OnCap(end, u, hitPoint, normal, table, 1e-4))
                {
                    continue;
                }
                double capPdf = CapDensity(end, table, CapNormal(end, hitPoint)) * (end == 0 ? startShare : endShare) / table.capShare[end];
                if (!(capPdf > 0))
                {
                    return 0;
                }
                SdfVector outward = CapNormal(end, hitPoint);
                Normal3f capNormal = pbrt.Normalize(settings.renderFromObject.Apply(new Normal3f((Float)outward.x, (Float)outward.y, (Float)outward.z)));
                Point3f capPoint = settings.renderFromObject.Apply(new Point3f((Float)hitPoint.x, (Float)hitPoint.y, (Float)hitPoint.z));
                return PatchSampling.AreaToSolidAngle(capPdf, capNormal, capPoint, ctx, wi);
            }
            // The hit of the end of a segment inside the strand (the sphere there, or the plane) is not on the tube
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
            double halfArc = VisibleArc(u, toward, out double middle, out double cosine, out double amplitude);
            if (!(halfArc > 0))
            {
                return 0;
            }
            double offset = angle - middle;
            offset -= 2 * Math.PI * Math.Round(offset / (2 * Math.PI));
            double areaPdf = tubeShare * ParameterDensity(table, weights, total, u) * AngleDensity(Math.Max(-halfArc, Math.Min(halfArc, offset)), halfArc, cosine, amplitude) / jacobian;
            return PatchSampling.AreaToSolidAngle(areaPdf, n, p, ctx, wi);
        }

        // A point of the tube or of a cap; with a reference point (object space) only of the side of the tube that faces it
        private pstd.optional<ShapeSample> SampleSurface(Point2f u, bool facing, SdfVector reference)
        {
            SamplingTable table = Table();
            if (!(table.total > 0))
            {
                return default;
            }
            Span<double> weights = stackalloc double[Nodes + 1];
            double total = 0, tubeShare = table.tubeShare, startShare = table.capShare[0], endShare = table.capShare[1];
            if (facing)
            {
                Shares(reference, table, weights, out total, out tubeShare, out startShare, out endShare);
            }
            double choice = u.x;
            if (choice >= tubeShare)
            {
                // A cap: the start, then the end
                choice -= tubeShare;
                int end = choice < startShare ? 0 : 1;
                double share = end == 0 ? startShare : endShare;
                if (end == 1)
                {
                    choice -= startShare;
                }
                if (!(share > 0) || !(table.capShare[end] > 0))
                {
                    return default;
                }
                pstd.optional<ShapeSample> cap = SampleCap(end, table, Math.Min(choice / share, 1 - 1e-12), u.y);
                if (!cap)
                {
                    return default;
                }
                ShapeSample result = cap.value();
                result.pdf *= (Float)(share / table.capShare[end]);
                return result;
            }
            double parameter = SampleParameter(table, weights, total, Math.Min(choice / tubeShare, 1 - 1e-12));
            double angle = 2 * Math.PI * u.y, anglePdf = 1 / (2 * Math.PI);
            if (facing)
            {
                double halfArc = VisibleArc(parameter, reference, out double center, out double cosine, out double amplitude);
                if (!(halfArc > 0))
                {
                    return default;
                }
                double offset = SampleAngle(u.y, halfArc, cosine, amplitude);
                angle = center + offset;
                anglePdf = AngleDensity(offset, halfArc, cosine, amplitude);
            }
            if (!Surface(parameter, angle, out Point3f p, out Normal3f n, out double jacobian) || !(jacobian > 0))
            {
                return default;
            }
            // With flat caps the solid is cut by the planes of the two ends: what the tube has beyond them is not there
            if (segment.FlatCaps && ObjectPoint(parameter, angle, out SdfVector position, out SdfVector outward) && !InsideSlab(position))
            {
                return default;
            }
            double pdf = tubeShare * ParameterDensity(table, weights, total, parameter) * anglePdf / jacobian;
            if (!(pdf > 0) || double.IsInfinity(pdf))
            {
                return default;
            }
            double around = angle / (2 * Math.PI);
            around -= Math.Floor(around);
            Vector3f pError = pbrt.gamma(8) * pbrt.Abs(new Vector3f(p.x, p.y, p.z));
            return new ShapeSample(new Interaction(new Point3fi(p, pError), n, new Point2f((Float)((segment.index + parameter) / segment.count), (Float)around)), (Float)pdf);
        }

        // Sampling close to the solid angle (not from a paper). Along the strand the tube is a cylinder at every u, and the
        // light that the ring at u sends to a point x is, with D = x - q(u) and the angle phi around the strand from the
        // direction of D, proportional to
        //     (area of the ring per angle) (n . (x - P))+ / |x - P|^3,   n . (x - P) = A (cos phi - k),
        // A = c |D across the strand|, k = (r - s D . t) / A (s the slope -r' / |q'|, c = sqrt(1 - s^2)): the cosine at the
        // surface is a cosine of the angle less a constant, positive on the arc |phi| < acos k that faces x. So
        //   - the angle is sampled with the density (cos phi - k) / (2 (sin a - k a)) on the arc of half width a = acos k,
        //     which takes the cosine at the surface out of the estimate;
        //   - the parameter u is sampled by the integral of the above over the arc with |x - P| taken as |D|,
        //     W(u) = (mass(u) / 2 pi) 2 A (sin a - k a) / |D|^3, at Nodes + 1 values of u joined by lines, which takes out the
        //     fall-off with the distance and the foreshortening of the strand seen along its length;
        //   - the tube and the two caps are chosen by the same measure (a cap by its area, how it is turned to x and its
        //     distance).
        // What is left in the estimate is the change of |x - P| around the ring and of the area of the ring around a bend.
        // A tenth of the choices of u and of the shares go by area, so that every point that faces x has a density
        // whatever the weights at the nodes say.
        private const int Nodes = 32;

        // The weights W at the nodes for a reference point, their integral, and the shares of the tube and the two caps
        private void Shares(SdfVector reference, SamplingTable table, Span<double> weights, out double total, out double tubeShare, out double startShare,
                            out double endShare)
        {
            total = 0;
            tubeShare = table.tubeShare;
            startShare = table.capShare[0];
            endShare = table.capShare[1];
            if (!SolidAngleSampling)
            {
                return;
            }
            for (int i = 0; i <= Nodes; ++i)
            {
                double u = (double)i / Nodes;
                double halfArc = VisibleArc(u, reference, out double middle, out double cosine, out double amplitude);
                double distance = (reference - segment.Position(u)).Length();
                double integral = amplitude > 0 ? 2 * amplitude * (Math.Sin(halfArc) - cosine * halfArc) : (halfArc > 0 ? 2 * Math.PI * (-cosine) : 0);
                weights[i] = halfArc > 0 && distance > 0 ? Math.Max(0, Mass(u) / (2 * Math.PI) * integral / (distance * distance * distance)) : 0;
            }
            for (int i = 0; i < Nodes; ++i)
            {
                total += 0.5 * (weights[i] + weights[i + 1]) / Nodes;
            }
            Span<double> caps = stackalloc double[2];
            for (int end = 0; end < 2; ++end)
            {
                caps[end] = 0;
                if (!(table.capShare[end] > 0))
                {
                    continue;
                }
                SdfVector axis = EndAxis(end, out double slope);
                SdfVector toReference = reference - segment.Position(end);
                double distance = toReference.Length();
                if (distance > 0)
                {
                    double turned = SdfVector.Dot(end == 0 ? -axis : axis, toReference) / distance;
                    caps[end] = table.capArea[end] * (0.25 + Math.Max(0, turned)) / (distance * distance);
                }
            }
            double all = total + caps[0] + caps[1];
            if (!(all > 0) || double.IsInfinity(all))
            {
                total = 0;
                return;
            }
            tubeShare = (1 - Defence) * total / all + Defence * table.tubeShare;
            startShare = (1 - Defence) * caps[0] / all + Defence * table.capShare[0];
            endShare = (1 - Defence) * caps[1] / all + Defence * table.capShare[1];
        }

        // The share of the choices that go by area
        private const double Defence = 0.1;

        // The parameter u for a number in [0, 1): by the weights at the nodes (total > 0), or by the area
        private double SampleParameter(SamplingTable table, Span<double> weights, double total, double choice)
        {
            if (total > 0 && choice >= Defence)
            {
                double target = (choice - Defence) / (1 - Defence) * total;
                for (int i = 0; i < Nodes; ++i)
                {
                    double mass = 0.5 * (weights[i] + weights[i + 1]) / Nodes;
                    if (target < mass || i == Nodes - 1)
                    {
                        // Inside the bin the density is the line from weights[i] to weights[i + 1]
                        double a = weights[i], b = weights[i + 1];
                        double x = mass > 0 ? Math.Max(0, Math.Min(1, target / mass)) : 0.5;
                        double within = Math.Abs(b - a) <= 1e-12 * (a + b) ? x : (Math.Sqrt(Math.Max(0, a * a + (b * b - a * a) * x)) - a) / (b - a);
                        return (i + Math.Max(0, Math.Min(1, within))) / Nodes;
                    }
                    target -= mass;
                }
            }
            double areaTarget = (total > 0 ? choice / Defence : choice) * table.total;
            int low = 0, high = table.cumulative.Length - 1;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (table.cumulative[middle + 1] > areaTarget)
                {
                    high = middle;
                }
                else
                {
                    low = middle + 1;
                }
            }
            double binMass = table.cumulative[low + 1] - table.cumulative[low];
            double fraction = binMass > 0 ? Math.Min(1, (areaTarget - table.cumulative[low]) / binMass) : 0.5;
            return (low + fraction) / table.bins;
        }

        // The density of SampleParameter at u
        private double ParameterDensity(SamplingTable table, Span<double> weights, double total, double u)
        {
            double area = AreaDensity(table, u);
            if (!(total > 0))
            {
                return area;
            }
            double position = Math.Max(0, Math.Min(1, u)) * Nodes;
            int bin = Math.Min(Nodes - 1, (int)position);
            double within = position - bin;
            return (1 - Defence) * ((1 - within) * weights[bin] + within * weights[bin + 1]) / total + Defence * area;
        }

        // The angle from the middle of the arc for a number in [0, 1): with the density (cos phi - k) / (2 (sin a - k a)), or
        // evenly over the arc (by area, and where the reference is on the axis of the ring)
        private double SampleAngle(double choice, double halfArc, double cosine, double amplitude)
        {
            if (!SolidAngleSampling || !(amplitude > 0))
            {
                return (2 * choice - 1) * halfArc;
            }
            // sin phi - k phi rises over the arc: the angle at which it is the wanted share of the way from one end to the other
            double wanted = (2 * choice - 1) * (Math.Sin(halfArc) - cosine * halfArc);
            double low = -halfArc, high = halfArc;
            for (int i = 0; i < 48; ++i)
            {
                double middle = 0.5 * (low + high);
                if (Math.Sin(middle) - cosine * middle < wanted)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }
            return 0.5 * (low + high);
        }

        private double AngleDensity(double offset, double halfArc, double cosine, double amplitude)
        {
            if (!SolidAngleSampling || !(amplitude > 0))
            {
                return 1 / (2 * halfArc);
            }
            double norm = 2 * (Math.Sin(halfArc) - cosine * halfArc);
            return norm > 0 ? Math.Max(0, Math.Cos(offset) - cosine) / norm : 0;
        }

        // Half of the arc of the angle around the strand at u whose points face the reference point (0 if no point of the ring
        // faces it, pi if all do), the middle of the arc, and the A and k of n . (x - P) = A (cos phi - k) (with A = 0, for a
        // reference on the axis of the ring, the cosine is n . (x - P) itself with the sign turned)
        private double VisibleArc(double u, SdfVector reference, out double middle, out double cosine, out double amplitude)
        {
            middle = 0;
            cosine = 1;
            amplitude = 0;
            SdfVector velocity = segment.Velocity(u);
            double speed = velocity.Length(), radius = segment.Radius(u);
            if (!(speed > 0) || !(radius > 0))
            {
                return 0;
            }
            double slope = -segment.RadiusDerivative(u) / speed;
            if (Math.Abs(slope) >= 1 - 1e-9)
            {
                return 0;
            }
            SdfVector axis = velocity / speed;
            SdfVector e1 = Frame(u, out SdfVector e2);
            SdfVector toReference = reference - segment.Position(u);
            double alongAxis = SdfVector.Dot(toReference, axis);
            double x = SdfVector.Dot(toReference, e1), y = SdfVector.Dot(toReference, e2);
            double across = Math.Sqrt(x * x + y * y), ring = Math.Sqrt(1 - slope * slope);
            // n . D = slope alongAxis + ring across cos(angle - middle) > radius
            double needed = radius - slope * alongAxis;
            if (!(across * ring > 0))
            {
                cosine = needed;
                return needed < 0 ? Math.PI : 0;
            }
            middle = Math.Atan2(y, x);
            amplitude = across * ring;
            cosine = needed / amplitude;
            if (cosine >= 1)
            {
                return 0;
            }
            return cosine <= -1 ? Math.PI : Math.Acos(cosine);
        }

        // Whether a point is between the planes of the two ends (always, without flat caps)
        private bool InsideSlab(SdfVector point)
        {
            if (!segment.FlatCaps)
            {
                return true;
            }
            SdfVector first = EndAxis(0, out double firstSlope), second = EndAxis(1, out double secondSlope);
            return SdfVector.Dot(point - segment.Position(0), first) >= 0 && SdfVector.Dot(point - segment.Position(1), second) <= 0;
        }

        // The unit tangent of the curve at an end, and the slope -r' / |q'| of the tube there
        private SdfVector EndAxis(int end, out double slope)
        {
            SdfVector velocity = segment.Velocity(end);
            double speed = velocity.Length();
            slope = speed > 0 ? -segment.RadiusDerivative(end) / speed : 0;
            return speed > 0 ? velocity / speed : new SdfVector(0, 0, 1);
        }

        // Whether the strand has a cap at the end (0 the start, 1 the end) of this segment that is sampled
        private bool HasCap(int end)
        {
            return (end == 0 ? segment.index == 0 : segment.index == segment.count - 1) && segment.Radius(end) > 0 && segment.Velocity(end).LengthSquared() > 0;
        }

        // The outward unit normal of the cap at a point of it (object space)
        private SdfVector CapNormal(int end, SdfVector point)
        {
            SdfVector axis = EndAxis(end, out double slope);
            if (segment.FlatCaps)
            {
                return end == 0 ? -axis : axis;
            }
            SdfVector radial = point - segment.Position(end);
            return radial.LengthSquared() > 0 ? radial.Normalized() : (end == 0 ? -axis : axis);
        }

        // Whether a point of the surface with its normal, found at the parameter u, is on the cap of an end that is sampled. A
        // point of a flat cap is found by a sphere near the end, at any parameter: it is told by lying in the plane of the end
        // with the normal of the plane (the tube crosses the plane too, with other normals). A point of a round cap is on the
        // sphere of the end, beyond the circle where the tube leaves it.
        private bool OnCap(int end, double u, SdfVector point, SdfVector normal, SamplingTable table, double margin)
        {
            if (!(table.capShare[end] > 0))
            {
                return false;
            }
            SdfVector axis = EndAxis(end, out double slope);
            SdfVector radial = point - segment.Position(end);
            double radius = segment.Radius(end), distance = radial.Length();
            double along = SdfVector.Dot(radial, axis);
            if (segment.FlatCaps)
            {
                return Math.Abs(SdfVector.Dot(normal, axis)) >= 1 - 1e-6 && Math.Abs(along) <= 1e-4 * table.capRadius[end] &&
                       distance <= table.capRadius[end] * (1 + 1e-6);
            }
            if (!(distance > 0) || (end == 0 ? u > 1e-6 : u < 1 - 1e-6))
            {
                return false;
            }
            along /= distance;
            return end == 0 ? along < slope - margin : along > slope + margin;
        }

        // The density by area (render space) of the cap at a point of it with the outward normal (object space)
        private double CapDensity(int end, SamplingTable table, SdfVector normal)
        {
            double scale = AreaScale(normal);
            if (!(table.capArea[end] > 0) || !(scale > 0))
            {
                return 0;
            }
            return table.capShare[end] / (table.capArea[end] * scale);
        }

        // The area in render space of a unit of area of object space with the given unit normal
        private double AreaScale(SdfVector normal)
        {
            SdfVector helper = Math.Abs(normal.x) < 0.9 ? new SdfVector(1, 0, 0) : new SdfVector(0, 1, 0);
            SdfVector first = SdfVector.Cross(normal, helper).Normalized(), second = SdfVector.Cross(normal, first);
            Vector3f a = settings.renderFromObject.Apply(new Vector3f((Float)first.x, (Float)first.y, (Float)first.z));
            Vector3f b = settings.renderFromObject.Apply(new Vector3f((Float)second.x, (Float)second.y, (Float)second.z));
            return pbrt.Length(pbrt.Cross(a, b));
        }

        // The point of a cap for two numbers in [0, 1) (object space), with its outward normal; false for a point of the disk
        // of a flat cap that is not on the surface
        private bool CapPoint(int end, SamplingTable table, double first, double second, out SdfVector point, out SdfVector normal)
        {
            SdfVector axis = EndAxis(end, out double slope);
            SdfVector e1 = Frame(end, out SdfVector e2);
            double angle = 2 * Math.PI * second;
            SdfVector across = e1 * Math.Cos(angle) + e2 * Math.Sin(angle);
            if (segment.FlatCaps)
            {
                normal = end == 0 ? -axis : axis;
                point = segment.Position(end) + across * (table.capRadius[end] * Math.Sqrt(first));
                // On the surface if a ray that comes at the plane from outside hits it there
                double offset = 1e-2 * table.capRadius[end];
                return segment.Intersect(point + normal * offset, -1 * normal, 0, 1e30, out double t, out double u, out SdfVector hitNormal) &&
                       Math.Abs(t - offset) <= 1e-3 * offset;
            }
            // Uniform in the cosine to the tangent: from -1 to the slope at the start, from the slope to 1 at the end
            double along = end == 0 ? -1 + first * (1 + slope) : slope + first * (1 - slope);
            normal = axis * along + across * Math.Sqrt(Math.Max(0, 1 - along * along));
            point = segment.Position(end) + normal * segment.Radius(end);
            return true;
        }

        private pstd.optional<ShapeSample> SampleCap(int end, SamplingTable table, double first, double second)
        {
            if (!CapPoint(end, table, first, second, out SdfVector point, out SdfVector normal))
            {
                return default;
            }
            double pdf = CapDensity(end, table, normal);
            if (!(pdf > 0) || double.IsInfinity(pdf))
            {
                return default;
            }
            Point3f p = settings.renderFromObject.Apply(new Point3f((Float)point.x, (Float)point.y, (Float)point.z));
            Normal3f n = pbrt.Normalize(settings.renderFromObject.Apply(new Normal3f((Float)normal.x, (Float)normal.y, (Float)normal.z)));
            if (settings.reverseOrientation ^ settings.transformSwapsHandedness)
            {
                n = -n;
            }
            Vector3f pError = pbrt.gamma(8) * pbrt.Abs(new Vector3f(p.x, p.y, p.z));
            return new ShapeSample(new Interaction(new Point3fi(p, pError), n, new Point2f((Float)((segment.index + end) / (double)segment.count), (Float)second)), (Float)pdf);
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
            // The area of the tube and the caps in render space (the cut of a flat cap by a count of the points of its disk)
            public double area;
            // The share of the samples that go to the tube and to the cap of each end
            public double tubeShare;
            public double[] capShare = new double[2];
            // The area in object space that a cap is sampled over (the part of the sphere, or the whole disk of a flat cap),
            // and the radius of that disk
            public double[] capArea = new double[2], capRadius = new double[2];
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
                const int nu = 512, nv = 32;
                for (int i = 0; i < nu; ++i)
                {
                    for (int j = 0; j < nv; ++j)
                    {
                        if (Surface((i + 0.5) / nu, 2 * Math.PI * (j + 0.5) / nv, out Point3f p, out Normal3f n, out double jacobian) &&
                            (!segment.FlatCaps || (ObjectPoint((i + 0.5) / nu, 2 * Math.PI * (j + 0.5) / nv, out SdfVector position, out SdfVector outward) && InsideSlab(position))))
                        {
                            sum += jacobian;
                        }
                    }
                }
                double tubeArea = sum * (2 * Math.PI / nv) / nu;
                result.area = tubeArea;
                double[] weight = new double[2];
                for (int end = 0; end < 2; ++end)
                {
                    if (!HasCap(end))
                    {
                        continue;
                    }
                    SdfVector axis = EndAxis(end, out double slope);
                    double radius = segment.Radius(end);
                    if (segment.FlatCaps)
                    {
                        result.capRadius[end] = FlatCapRadius(end);
                        result.capArea[end] = Math.PI * result.capRadius[end] * result.capRadius[end];
                    }
                    else
                    {
                        result.capArea[end] = 2 * Math.PI * radius * radius * Math.Max(0, end == 0 ? 1 + slope : 1 - slope);
                    }
                    if (!(result.capArea[end] > 0))
                    {
                        continue;
                    }
                    // The area in render space: the points of a grid that are on the surface, each with its scale
                    result.capShare[end] = 1;
                    const int grid = 96;
                    double capSum = 0;
                    for (int i = 0; i < grid; ++i)
                    {
                        for (int j = 0; j < grid; ++j)
                        {
                            if (CapPoint(end, result, (i + 0.5) / grid, (j + 0.5) / grid, out SdfVector point, out SdfVector normal))
                            {
                                capSum += AreaScale(normal);
                            }
                        }
                    }
                    weight[end] = result.capArea[end] * capSum / (grid * grid);
                    result.area += weight[end];
                }
                double all = tubeArea + weight[0] + weight[1];
                result.tubeShare = all > 0 ? tubeArea / all : 1;
                result.capShare[0] = all > 0 ? weight[0] / all : 0;
                result.capShare[1] = all > 0 ? weight[1] / all : 0;
                table = result;
            }
            return result;
        }

        // The radius of a disk about the end point, in the plane of the end, that holds the cut of the solid by that plane. A
        // sphere of the strand reaches the plane if its center is no farther from it than the largest radius; the disk holds
        // the centers of those, seen in the plane, and the largest radius around them. The centers are looked at in steps,
        // with what the curve can move in half a step added.
        private double FlatCapRadius(int end)
        {
            SdfVector axis = EndAxis(end, out double slope);
            SdfVector origin = segment.Position(end);
            double[] radii = segment.Radii;
            double largest = Math.Max(Math.Max(radii[0], radii[1]), Math.Max(radii[2], radii[3]));
            SdfVector[] points = segment.ControlPoints;
            double speed = 3 * Math.Max((points[1] - points[0]).Length(), Math.Max((points[2] - points[1]).Length(), (points[3] - points[2]).Length()));
            const int steps = 256;
            double slack = 0.5 * speed / steps, reach = 0;
            for (int i = 0; i <= steps; ++i)
            {
                SdfVector offset = segment.Position((double)i / steps) - origin;
                double depth = Math.Abs(SdfVector.Dot(offset, axis));
                if (depth <= largest + slack)
                {
                    reach = Math.Max(reach, (offset - axis * SdfVector.Dot(offset, axis)).Length() + slack);
                }
            }
            return reach + largest;
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

        // In double precision: the density of the area is a difference of such points a hundred thousandth of the parameter
        // apart, which the single precision of Transform.Apply left right to three places only
        private SdfVector ToRender(SdfVector point)
        {
            SquareMatrix4 m = settings.renderFromObject.GetMatrix();
            return new SdfVector(m[0][0] * point.x + m[0][1] * point.y + m[0][2] * point.z + m[0][3],
                                 m[1][0] * point.x + m[1][1] * point.y + m[1][2] * point.z + m[1][3],
                                 m[2][0] * point.x + m[2][1] * point.y + m[2][2] * point.z + m[2][3]);
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
            // The hit is the entry into a sphere near the envelope of the spheres, so its normal is tilted a little from the normal of the envelope (the angle to the tangent is
            // -r'/|q'|): the normal of the tube is corrected to that, which the hair material needs (its frame is perpendicular to the strand)
            if (tangent.LengthSquared() > 0)
            {
                SdfVector axisUnit = tangent.Normalized();
                double slope = -segment.RadiusDerivative(u) / tangent.Length();
                double alongNormal = SdfVector.Dot(normalObject, axisUnit);
                if (Math.Abs(alongNormal - slope) < 0.05 && Math.Abs(slope) < 0.999)
                {
                    SdfVector perpendicular = normalObject - axisUnit * alongNormal;
                    if (perpendicular.LengthSquared() > 1e-24)
                    {
                        normalObject = axisUnit * slope + perpendicular.Normalized() * Math.Sqrt(1 - slope * slope);
                    }
                }
            }
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
            // v for the hair material, as for the curves of pbrt: the offset of the hit across the strand in [-1, 1] as seen along the ray (the component of the normal along
            // d x tangent), as (h + 1) / 2
            Vector3f rayDirection = settings.objectFromRender.Apply(ray.d);
            SdfVector viewed = new SdfVector(rayDirection.x, rayDirection.y, rayDirection.z);
            SdfVector across = SdfVector.Cross(viewed, axis);
            double h = across.LengthSquared() > 0 ? Math.Max(-1, Math.Min(1, SdfVector.Dot(normalObject, across.Normalized()))) : 0;
            Point2f uv = new Point2f((Float)((segment.index + u) / segment.count), (Float)(0.5 * (1 + h)));
            bool flipNormal = settings.reverseOrientation ^ settings.transformSwapsHandedness;
            Vector3f pError = pbrt.gamma(8) * pbrt.Abs(new Vector3f(p.x, p.y, p.z));
            return new SurfaceInteraction(new Point3fi(p, pError), uv, -ray.d, dpdu, dpdv, new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), ray.time, flipNormal,
                                          strandColors);
        }

        private readonly BezierSurfaceSettings settings;
        private readonly RocapSegment segment;
        // The number of the registered colors of the strand (StrandColors), the face index of the hits; 0 without
        private readonly int strandColors;
        private readonly Bounds3f bounds;
        private SamplingTable table;
        private SdfVector reference;
    }

    public static class Rocaps
    {
        // The Bezier control values of the n curves of a basis of RenderMan with the values g (4 + (n - 1) step of them), four
        // for each curve: curve s has the values g[s step .. s step + 3], and its Bezier values are B^-1 M g with the matrices M
        // of the basis and B of bezier, which is the same cubic exactly.
        public static double[] ToBezierCurves(double[] g, double[,] basis, int step)
        {
            PatchBases.TryGet("bezier", out double[,] bezier);
            double[,] toBezier = PatchBases.Multiply(PatchBases.Inverse(bezier), basis);
            int curves = (g.Length - 4) / step + 1;
            double[] values = new double[4 * curves];
            for (int s = 0; s < curves; ++s)
            {
                for (int i = 0; i < 4; ++i)
                {
                    double value = 0;
                    for (int k = 0; k < 4; ++k)
                    {
                        value += toBezier[i, k] * g[s * step + k];
                    }
                    values[4 * s + i] = value;
                }
            }
            return values;
        }

        // The chain of 3n + 1 values of n Bezier curves that meet, from the four values of each
        private static double[] Chain(double[] values)
        {
            int curves = values.Length / 4;
            double[] chain = new double[3 * curves + 1];
            for (int s = 0; s < curves; ++s)
            {
                chain[3 * s] = values[4 * s];
                chain[3 * s + 1] = values[4 * s + 1];
                chain[3 * s + 2] = values[4 * s + 2];
            }
            chain[3 * curves] = values[values.Length - 1];
            return chain;
        }

        // "string basis" ("bezier"; "b-spline", "catmull-rom", "hermite", "power": the bases of RenderMan's curves) and "integer step" (the step of
        // the basis: 3 for bezier and power, 1 for b-spline and catmull-rom, 2 for hermite): "P" has 4 + n step points for n cubic curves, and
        // the radii and the colors that are given one for each point follow the same basis as the points. Every curve is changed to its
        // Bezier form, which the change of basis does exactly. (The colors are kept as one chain along the strand: with hermite or power
        // values whose curves do not meet, the color at the end of a curve is that of the start of the next.)
        // "point3 P" (the control points of the chain of cubic Bezier curves: 3n + 1 points), "float radius" (one value, or one for each point),
        // "float width0" and "float width1" (diameters at the two ends when "radius" is absent, 0.02 and 0.02 of the diagonal), "rgb Cs" (the color of the strand, one for all or one for each point, read by a "strandcolor" texture with "local" true), "bool flatcaps" (disks at the ends instead of spheres), "epsilon" (a fraction of the
        // diagonal of the points that keeps rays that leave the surface from hitting it again)
        public static pstd.vector<Shape> Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation, ParameterDictionary parameters,
                                                FileLoc loc)
        {
            List<Point3f> pointList = parameters.GetPoint3fArray("P");
            List<float> radiusList = parameters.GetFloatArray("radius");
            string basisName = parameters.GetOneString("basis", "bezier");
            if (!PatchBases.TryGet(basisName, out double[,] basis))
            {
                pbrt.ErrorExit(loc, "rocap: unknown basis \"" + basisName + "\" (bezier, b-spline, catmull-rom, hermite, power).");
            }
            int step = parameters.GetOneInt("step", PatchBases.DefaultStep(basisName));
            if (step < 1 || pointList.Count < 4 || (pointList.Count - 4) % step != 0)
            {
                pbrt.ErrorExit(loc, "rocap: \"P\" needs 4 + n * step control points (n cubic curves of the basis \"" + basisName + "\" with the step " + step +
                                    "), n >= 0.");
            }
            bool bezierChain = basisName == "bezier" && step == 3;
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

            List<RGB> colorList = parameters.GetRGBArray("Cs");
            if (colorList.Count > 1 && colorList.Count != points.Length)
            {
                pbrt.ErrorExit(loc, "rocap: \"Cs\" needs one color or one for each point of \"P\".");
            }
            RGB[] colors = colorList.ToArray();
            if (!bezierChain)
            {
                // The Bezier curves of the curves of the basis, four values each: the points, the radii and the colors alike
                double[] red = new double[colors.Length], green = new double[colors.Length], blue = new double[colors.Length];
                for (int i = 0; i < colors.Length; ++i)
                {
                    red[i] = colors[i].r;
                    green[i] = colors[i].g;
                    blue[i] = colors[i].b;
                }
                double[] x = new double[points.Length], y = new double[points.Length], z = new double[points.Length];
                for (int i = 0; i < points.Length; ++i)
                {
                    x[i] = points[i].x;
                    y[i] = points[i].y;
                    z[i] = points[i].z;
                }
                x = ToBezierCurves(x, basis, step);
                y = ToBezierCurves(y, basis, step);
                z = ToBezierCurves(z, basis, step);
                points = new SdfVector[x.Length];
                for (int i = 0; i < points.Length; ++i)
                {
                    points[i] = new SdfVector(x[i], y[i], z[i]);
                }
                if (radiusList.Count > 1)
                {
                    radii = ToBezierCurves(radii, basis, step);
                    for (int i = 0; i < radii.Length; ++i)
                    {
                        radii[i] = Math.Max(0, radii[i]);
                    }
                }
                else
                {
                    // One radius, or the two widths of the ends: along the strand, so that it starts and ends with them
                    radii = new double[points.Length];
                    for (int i = 0; i < radii.Length; ++i)
                    {
                        double along = (3 * (i / 4) + i % 4) / (0.75 * radii.Length);
                        radii[i] = radiusList.Count == 1 ? radiusList[0] : 0.5 * (width0 + (width1 - width0) * along);
                    }
                }
                if (colors.Length > 1)
                {
                    red = Chain(ToBezierCurves(red, basis, step));
                    green = Chain(ToBezierCurves(green, basis, step));
                    blue = Chain(ToBezierCurves(blue, basis, step));
                    colors = new RGB[red.Length];
                    for (int i = 0; i < colors.Length; ++i)
                    {
                        colors[i] = new RGB((Float)Math.Max(0, red[i]), (Float)Math.Max(0, green[i]), (Float)Math.Max(0, blue[i]));
                    }
                }
            }
            int strandColors = colors.Length > 0 ? StrandColors.Register(colors, true) : 0;
            bool flatCaps = parameters.GetOneBool("flatcaps", false);
            // "bool solidanglesampling" (true): the samples of an emitting strand close to its solid angle from the lit point; false: by the area of the side that faces it
            bool solidAngle = parameters.GetOneBool("solidanglesampling", true);
            // The curves of a chain share their ends (3n + 1 values); those of another basis have four values each
            int stride = bezierChain ? 3 : 4;
            int count = bezierChain ? (points.Length - 1) / 3 : points.Length / 4;
            pstd.vector<Shape> shapes = new pstd.vector<Shape>();
            for (int s = 0; s < count; ++s)
            {
                SdfVector[] p = new SdfVector[] { points[stride * s], points[stride * s + 1], points[stride * s + 2], points[stride * s + 3] };
                double[] r = new double[] { radii[stride * s], radii[stride * s + 1], radii[stride * s + 2], radii[stride * s + 3] };
                shapes.push_back(new RocapShape(settings, new RocapSegment(p, r, s, count) { FlatCaps = flatCaps }, strandColors) { SolidAngleSampling = solidAngle });
            }
            return shapes;
        }
    }
}
