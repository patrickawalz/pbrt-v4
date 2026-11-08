// (not in pbrt-v4) shapes_ngon.cs
//
// Shape "ngonmesh": a mesh of planar convex polygons with up to MaxCornerCount corners, intersected, sampled and shaded as polygons. For polygons of any corner
// count (a quad cage, the faces of a subdivision or of a modeller's mesh, a polygonal light).
//
//  * Intersection. A polygon is the fan of triangles (v0, v[j + 1], v[j + 2]). The ray is moved to the ray coordinate system once (as the watertight triangle
//    test of Woop et al. does), and the edge functions of the fan (the sides and the diagonals from v0) are evaluated in double precision, which makes their signs
//    exact for the single precision vertices. Each diagonal is evaluated once and used with opposite signs for the two triangles it separates, and a side is
//    evaluated from its two projected vertices only, so the function of (b, a) is exactly the negative of the function of (a, b). A fan of such triangles is
//    watertight: the ray cannot pass between two triangles of the fan, between two polygons, or between a polygon and a Quad or a Triangle, that share an edge
//    and its two vertices (as those use the same edge functions). The distance and the weights are those of the triangle that was hit, with the error bounds of
//    a triangle (Quad.TestTriangle), so that the rays spawned from the hit are offset correctly. The interaction (the uv, dpdu, dpdv, the shading normal) is that of
//    the Triangle of the fan: the texture coordinates are those of the mesh or, if it has none, the coordinates of the polygon in its plane, scaled to the
//    bounding box of the polygon.
//  * Sampling. By area: a triangle of the fan, chosen by area, then a uniform point of it (density 1 / Area). By solid angle, from a point with a shading normal,
//    with "string sampling" "projected" (the default): proportional to the projected solid angle of the polygon, the exact cosine weighted density |cos| / A of
//    polygon_sampling.cs (Peters 2021), for any corner count and for both sides of the shading normal. Without a normal (a point in a medium), or with
//    "solidangle": one of the spherical triangles of the fan, chosen by solid angle, so the density is 1 / (solid angle of the polygon). A polygon of a small solid
//    angle, one seen edge on, and a reference point in its plane are sampled by area and the density is converted to solid angle.
//  * Polygons that are not planar and convex (within a tolerance relative to their size), or that are degenerate, are not made into Ngon shapes: each is
//    triangulated (ear clipping) into ordinary Triangle shapes.
//
// The corners of a polygon are given in order around it, counterclockwise when seen from the side the normal points to.

using System;
using System.Collections.Generic;

namespace pbrt
{
    public sealed unsafe class Ngon : Shape
    {
        public const int MaxCornerCount = 11;

        private const Float MinSphericalSampleArea = 3e-4f;
        private const Float MaxSphericalSampleArea = 6.22f;
        private const double MinProjectedSampleArea = 1e-8;

        // Tolerances of the test that makes a polygon an Ngon: relative to the length of the longest diagonal
        private const double PlanarTolerance = 1e-5;
        private const double ConvexTolerance = 1e-6;
        private const double DegenerateTolerance = 1e-6;

        // A corner whose turn has a sine below this is on the line of its neighbors: it is not a corner of the polygon sampled by projected solid angle (that
        // needs no three corners on a great circle). The intersection uses all the corners.
        private const double CollinearSine = 1e-9;

        [ThreadStatic]
        private static ProjectedSolidAnglePolygon upperPolygon;
        [ThreadStatic]
        private static ProjectedSolidAnglePolygon lowerPolygon;
        [ThreadStatic]
        private static double[] localX;
        [ThreadStatic]
        private static double[] localY;
        [ThreadStatic]
        private static double[] localZ;
        [ThreadStatic]
        private static Float[] omegaScratch;

        private TriangleMesh mesh;
        private int firstVertex;
        private int firstTriangle;
        private int cornerCount;
        private Float area;
        private Float[] fanAreaCdf;
        private Vector3f planeNormal;
        private int[] sampleCorners;
        private bool projectedSampling;

        // The hit of a triangle of the fan: the weights are those of (v0, v[fan + 1], v[fan + 2])
        private struct FanHit
        {
            public int fan;
            public double w0;
            public double w1;
            public double w2;
            public Float t;
        }

        public Ngon(TriangleMesh mesh, int firstVertex, int firstTriangle, int cornerCount, int[] sampleCorners, bool projectedSampling)
        {
            this.mesh = mesh;
            this.firstVertex = firstVertex;
            this.firstTriangle = firstTriangle;
            this.cornerCount = cornerCount;
            this.sampleCorners = sampleCorners;
            this.projectedSampling = projectedSampling;

            // The areas of the fan and the normal of the plane, in double precision
            Point3f p0 = Corner(0);
            fanAreaCdf = new Float[cornerCount - 2];
            double total = 0;
            double nx = 0, ny = 0, nz = 0;
            double[] cumulative = new double[cornerCount - 2];
            for (int j = 0; j < cornerCount - 2; ++j)
            {
                Point3f b = Corner(j + 1);
                Point3f c = Corner(j + 2);
                double ex = (double)b.x - p0.x, ey = (double)b.y - p0.y, ez = (double)b.z - p0.z;
                double fx = (double)c.x - p0.x, fy = (double)c.y - p0.y, fz = (double)c.z - p0.z;
                double cx = ey * fz - ez * fy, cy = ez * fx - ex * fz, cz = ex * fy - ey * fx;
                nx += cx;
                ny += cy;
                nz += cz;
                total += 0.5 * Math.Sqrt(cx * cx + cy * cy + cz * cz);
                cumulative[j] = total;
            }
            for (int j = 0; j < cornerCount - 2; ++j)
            {
                fanAreaCdf[j] = total > 0 ? (Float)(cumulative[j] / total) : (Float)(j + 1) / (cornerCount - 2);
            }
            fanAreaCdf[cornerCount - 3] = 1;
            area = (Float)total;
            double length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            planeNormal = length > 0 ? new Vector3f((Float)(nx / length), (Float)(ny / length), (Float)(nz / length)) : new Vector3f(0, 0, 1);
        }

        private Point3f Corner(int k)
        {
            return mesh.p[firstVertex + k];
        }

        internal Point3f CornerPoint(int k)
        {
            return Corner(k);
        }

        internal Vector3f PlaneNormal
        {
            get
            {
                return planeNormal;
            }
        }

        // The sample at a point of the plane of the polygon (inside it, to rounding), with the given density
        internal pstd.optional<ShapeSample> SampleAtPoint(double px, double py, double pz, Float time, Float pdf)
        {
            int fan;
            double b0, b1, b2;
            FanWeights(px, py, pz, out fan, out b0, out b1, out b2);
            return SampleAtFan(fan, b0, b1, b2, time, true, pdf);
        }

        // Whether the points (render space, in order around the polygon) are a planar, convex, non-degenerate polygon, and if so the corners that the sampling
        // by projected solid angle uses (those that are not on the line of their neighbors; null if all are).
        public static bool Classify(double[][] c, out int[] keptCorners)
        {
            keptCorners = null;
            int n = c.Length;
            if (n < 3 || n > MaxCornerCount)
            {
                return false;
            }

            double diagonal = 0;
            for (int i = 0; i < n; ++i)
            {
                for (int j = i + 1; j < n; ++j)
                {
                    diagonal = Math.Max(diagonal, Length(Sub(c[i], c[j])));
                }
            }
            if (diagonal == 0)
            {
                return false;
            }
            for (int i = 0; i < n; ++i)
            {
                if (Length(Sub(c[(i + 1) % n], c[i])) <= DegenerateTolerance * diagonal)
                {
                    return false;
                }
            }

            // The normal of the polygon (Newell's), relative to the center for accuracy
            double[] center = new double[3];
            for (int i = 0; i < n; ++i)
            {
                for (int k = 0; k < 3; ++k)
                {
                    center[k] += c[i][k] / n;
                }
            }
            double[] normal = new double[3];
            for (int i = 0; i < n; ++i)
            {
                double[] cross = Cross(Sub(c[i], center), Sub(c[(i + 1) % n], center));
                for (int k = 0; k < 3; ++k)
                {
                    normal[k] += cross[k];
                }
            }
            double normalLength = Length(normal);
            if (normalLength == 0)
            {
                return false;
            }
            for (int k = 0; k < 3; ++k)
            {
                normal[k] /= normalLength;
            }

            // Planar: every corner within a tolerance of the plane through the center
            for (int i = 0; i < n; ++i)
            {
                if (Math.Abs(Dot(normal, Sub(c[i], center))) > PlanarTolerance * diagonal)
                {
                    return false;
                }
            }

            // Convex: no turn to the side opposite to the normal (more than a tolerance), and the turns add up to one revolution (a star does not)
            double totalTurn = 0;
            List<int> kept = new List<int>();
            for (int i = 0; i < n; ++i)
            {
                double[] incoming = Sub(c[i], c[(i + n - 1) % n]);
                double[] outgoing = Sub(c[(i + 1) % n], c[i]);
                double sine = Dot(normal, Cross(incoming, outgoing)) / (Length(incoming) * Length(outgoing));
                double cosine = Dot(incoming, outgoing) / (Length(incoming) * Length(outgoing));
                if (sine < -ConvexTolerance)
                {
                    return false;
                }
                totalTurn += Math.Atan2(sine, cosine);
                if (Math.Abs(sine) > CollinearSine || cosine < 0)
                {
                    kept.Add(i);
                }
            }
            if (Math.Abs(totalTurn - 2 * Math.PI) > 1e-6)
            {
                return false;
            }
            if (kept.Count < 3)
            {
                return false;
            }
            if (kept.Count != n)
            {
                keptCorners = kept.ToArray();
            }
            return true;
        }

        private static double[] Sub(double[] a, double[] b)
        {
            return new double[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        }

        private static double[] Cross(double[] a, double[] b)
        {
            return new double[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
        }

        private static double Dot(double[] a, double[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }

        private static double Length(double[] a)
        {
            return Math.Sqrt(Dot(a, a));
        }

        // The triangle of the fan that the ray hits, if any: the first whose edge functions (all in double precision) pass the test
        private bool IntersectFan(Ray ray, Float tMax, out FanHit hit)
        {
            hit = default;
            Quad.RayFrame frame = new Quad.RayFrame(ray);
            Float x0, y0, z0, x1, y1, z1, x2, y2, z2;
            frame.Project(Corner(0), out x0, out y0, out z0);
            frame.Project(Corner(1), out x1, out y1, out z1);
            // The diagonal from v0 to the corner that starts the triangle: evaluated once, and used with the opposite sign by the triangle that ends at it
            double diagonalFirst = Quad.EdgeFunction(x0, y0, x1, y1);
            for (int j = 0; j < cornerCount - 2; ++j)
            {
                frame.Project(Corner(j + 2), out x2, out y2, out z2);
                double diagonalSecond = Quad.EdgeFunction(x0, y0, x2, y2);
                double side = Quad.EdgeFunction(x1, y1, x2, y2);
                double w0, w1, w2;
                Float t;
                if (Quad.TestTriangle(side, -diagonalSecond, diagonalFirst, x0, y0, z0, x1, y1, z1, x2, y2, z2, tMax, out w0, out w1, out w2, out t))
                {
                    hit.fan = j;
                    hit.w0 = w0;
                    hit.w1 = w1;
                    hit.w2 = w2;
                    hit.t = t;
                    return true;
                }
                x1 = x2;
                y1 = y2;
                z1 = z2;
                diagonalFirst = diagonalSecond;
            }
            return false;
        }

        public override pstd.optional<ShapeIntersection> Intersect(Ray ray, Float tMax = Infinity)
        {
            FanHit hit;
            if (!IntersectFan(ray, tMax, out hit))
            {
                return default;
            }
            TriangleIntersection ti = new TriangleIntersection((Float)hit.w0, (Float)hit.w1, (Float)hit.w2, hit.t);
            SurfaceInteraction isect = Triangle.InteractionFromIntersection(mesh, firstTriangle + hit.fan, ti, ray.time, -ray.d);
            return new ShapeIntersection(isect, hit.t);
        }

        public override bool IntersectP(Ray ray, Float tMax = Infinity)
        {
            FanHit hit;
            return IntersectFan(ray, tMax, out hit);
        }

        public override Float Area()
        {
            return area;
        }

        public override Bounds3f Bounds()
        {
            Point3f first = Corner(0);
            Bounds3f bounds = new Bounds3f(first, first);
            for (int k = 1; k < cornerCount; ++k)
            {
                Point3f c = Corner(k);
                bounds = pbrt.Union(bounds, new Bounds3f(c, c));
            }
            return bounds;
        }

        public override DirectionCone NormalBounds()
        {
            Normal3f n = (Normal3f)planeNormal;
            int v0 = firstVertex;
            if (mesh.n != null)
            {
                Normal3f ns = mesh.n[v0];
                for (int k = 1; k < cornerCount; ++k)
                {
                    ns += mesh.n[firstVertex + k];
                }
                n = pbrt.FaceForward(n, ns);
            }
            else if (mesh.reverseOrientation ^ mesh.transformSwapsHandedness)
            {
                n = -n;
            }
            return new DirectionCone((Vector3f)n);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ Ngon corners: %d firstVertex: %d area: %f ]", cornerCount, firstVertex, Area());
        }

        // The weights, in the triangle of the fan that holds the point of the plane of the polygon, of the corners of that triangle
        private void FanWeights(double px, double py, double pz, out int fan, out double b0, out double b1, out double b2)
        {
            fan = 0;
            b0 = 1;
            b1 = 0;
            b2 = 0;
            double bestScore = double.NegativeInfinity;
            Point3f a = Corner(0);
            for (int j = 0; j < cornerCount - 2; ++j)
            {
                Point3f b = Corner(j + 1);
                Point3f c = Corner(j + 2);
                double e1x = (double)b.x - a.x, e1y = (double)b.y - a.y, e1z = (double)b.z - a.z;
                double e2x = (double)c.x - a.x, e2y = (double)c.y - a.y, e2z = (double)c.z - a.z;
                double nx = e1y * e2z - e1z * e2y, ny = e1z * e2x - e1x * e2z, nz = e1x * e2y - e1y * e2x;
                double nn = nx * nx + ny * ny + nz * nz;
                if (nn == 0)
                {
                    continue;
                }
                double dx = px - a.x, dy = py - a.y, dz = pz - a.z;
                double wb = ((dy * e2z - dz * e2y) * nx + (dz * e2x - dx * e2z) * ny + (dx * e2y - dy * e2x) * nz) / nn;
                double wc = ((e1y * dz - e1z * dy) * nx + (e1z * dx - e1x * dz) * ny + (e1x * dy - e1y * dx) * nz) / nn;
                double wa = 1 - wb - wc;
                double score = Math.Min(wa, Math.Min(wb, wc));
                if (score > bestScore)
                {
                    bestScore = score;
                    fan = j;
                    b0 = wa;
                    b1 = wb;
                    b2 = wc;
                }
            }
            // A point a rounding error outside of the triangle is moved onto it
            b0 = Math.Max(b0, 0.0);
            b1 = Math.Max(b1, 0.0);
            b2 = Math.Max(b2, 0.0);
            double sum = b0 + b1 + b2;
            b0 /= sum;
            b1 /= sum;
            b2 /= sum;
        }

        // The sample of the point of the triangle `fan` with the weights b of its corners (v0, v[fan + 1], v[fan + 2])
        private ShapeSample SampleAtFan(int fan, double b0, double b1, double b2, Float time, bool hasTime, Float pdf)
        {
            int v0 = firstVertex, v1 = firstVertex + fan + 1, v2 = firstVertex + fan + 2;
            Point3f p0 = mesh.p[v0], p1 = mesh.p[v1], p2 = mesh.p[v2];
            Float f0 = (Float)b0, f1 = (Float)b1, f2 = (Float)b2;
            Point3f p = f0 * p0 + f1 * p1 + f2 * p2;
            Point3f pAbsSum = pbrt.Abs(f0 * p0) + pbrt.Abs(f1 * p1) + pbrt.Abs(f2 * p2);
            Vector3f pError = pbrt.gamma(6) * (Vector3f)pAbsSum;

            Normal3f n = (Normal3f)planeNormal;
            if (mesh.n != null)
            {
                Normal3f ns = f0 * mesh.n[v0] + f1 * mesh.n[v1] + f2 * mesh.n[v2];
                n = pbrt.FaceForward(n, ns);
            }
            else if (mesh.reverseOrientation ^ mesh.transformSwapsHandedness)
            {
                n = -n;
            }
            Point2f st = mesh.uv != null ? f0 * mesh.uv[v0] + f1 * mesh.uv[v1] + f2 * mesh.uv[v2] : new Point2f(0, 0);
            Interaction intr = hasTime ? new Interaction(new Point3fi(p, pError), n, time, st) : new Interaction(new Point3fi(p, pError), n, st);
            return new ShapeSample(intr, pdf);
        }

        public override Float PDF(Interaction intr)
        {
            return 1 / Area();
        }

        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            int fan = 0;
            while (fan < cornerCount - 3 && u[0] >= fanAreaCdf[fan])
            {
                ++fan;
            }
            Float low = fan == 0 ? 0 : fanAreaCdf[fan - 1];
            Float high = fanAreaCdf[fan];
            Point2f uTriangle = new Point2f(std.min((u[0] - low) / (high - low), (Float)pbrt.OneMinusEpsilon), u[1]);
            pstd.array3<Float> b = pbrt.SampleUniformTriangle(uTriangle);
            return SampleAtFan(fan, b[0], b[1], b[2], 0, false, 1 / Area());
        }

        // Whether the reference point is in the plane of the polygon (within its error bounds)
        private bool ReferencePointOnPlane(ShapeSampleContext ctx)
        {
            const Float rounding = 4 * pbrt.MachineEpsilon;
            Vector3f toReference = ctx.p() - Corner(0);
            Vector3f absoluteNormal = pbrt.Abs(planeNormal);
            Float bound = pbrt.Dot(absoluteNormal, ctx.pi.Error() + rounding * pbrt.Abs(toReference));
            return std.abs(pbrt.Dot(planeNormal, toReference)) <= bound;
        }

        // The solid angles of the triangles of the fan seen from the reference point, and whether the polygon is sampled by solid angle (as spherical triangles)
        private bool SamplesBySolidAngle(ShapeSampleContext ctx, out Float total)
        {
            total = 0;
            if (omegaScratch == null)
            {
                omegaScratch = new Float[MaxCornerCount];
            }
            Point3f p = ctx.p();
            Vector3f first = pbrt.Normalize(Corner(0) - p);
            Vector3f previous = pbrt.Normalize(Corner(1) - p);
            for (int j = 0; j < cornerCount - 2; ++j)
            {
                Vector3f next = pbrt.Normalize(Corner(j + 2) - p);
                Float omega = pbrt.SphericalTriangleArea(first, previous, next);
                if (omega < MinSphericalSampleArea || omega > MaxSphericalSampleArea)
                {
                    return false;
                }
                omegaScratch[j] = omega;
                total += omega;
                previous = next;
            }
            return !ReferencePointOnPlane(ctx);
        }

        // The parts of the polygon above and below the horizon of the reference point, for the sampling by projected solid angle, and their projected solid angles.
        // False if the polygon is not sampled that way from the reference point.
        private bool PrepareProjected(ShapeSampleContext ctx, out Quad.Basis basis, out ProjectedSolidAnglePolygon upper, out ProjectedSolidAnglePolygon lower,
                                      out double upperArea, out double lowerArea)
        {
            basis = default;
            upper = null;
            lower = null;
            upperArea = 0;
            lowerArea = 0;
            if (!projectedSampling || ctx.ns == new Normal3f(0, 0, 0) || ReferencePointOnPlane(ctx))
            {
                return false;
            }
            basis = new Quad.Basis(ctx.ns);
            if (upperPolygon == null)
            {
                upperPolygon = new ProjectedSolidAnglePolygon();
                lowerPolygon = new ProjectedSolidAnglePolygon();
                localX = new double[MaxCornerCount];
                localY = new double[MaxCornerCount];
                localZ = new double[MaxCornerCount];
            }
            Point3f reference = ctx.p();
            int count = sampleCorners != null ? sampleCorners.Length : cornerCount;
            for (int i = 0; i < count; ++i)
            {
                Point3f c = Corner(sampleCorners != null ? sampleCorners[i] : i);
                double ox = (double)c.x - reference.x;
                double oy = (double)c.y - reference.y;
                double oz = (double)c.z - reference.z;
                localX[i] = ox * basis.xx + oy * basis.xy + oz * basis.xz;
                localY[i] = ox * basis.yx + oy * basis.yy + oz * basis.yz;
                localZ[i] = ox * basis.zx + oy * basis.zy + oz * basis.zz;
            }
            upper = upperPolygon;
            lower = lowerPolygon;
            if (upper.Prepare(count, localX, localY, localZ, 1))
            {
                upperArea = upper.ProjectedSolidAngle;
            }
            if (lower.Prepare(count, localX, localY, localZ, -1))
            {
                lowerArea = lower.ProjectedSolidAngle;
            }
            return upperArea + lowerArea > MinProjectedSampleArea;
        }

        private pstd.optional<ShapeSample> SampleProjected(ShapeSampleContext ctx, Point2f u, Quad.Basis basis, ProjectedSolidAnglePolygon upper,
                                                           ProjectedSolidAnglePolygon lower, double upperArea, double lowerArea)
        {
            double total = upperArea + lowerArea;
            double pUpper = upperArea / total;
            double u0 = u[0];
            ProjectedSolidAnglePolygon chosen;
            double sign;
            if (u0 < pUpper)
            {
                chosen = upper;
                sign = 1;
                u0 = u0 / pUpper;
            }
            else
            {
                chosen = lower;
                sign = -1;
                u0 = (u0 - pUpper) / (1 - pUpper);
            }
            const double belowOne = 1 - 1.1102230246251565e-16;
            double lx, ly, lz;
            chosen.Sample(Math.Min(u0, belowOne), Math.Min((double)u[1], belowOne), out lx, out ly, out lz);
            lz *= sign;
            double wx = lx * basis.xx + ly * basis.yx + lz * basis.zx;
            double wy = lx * basis.xy + ly * basis.yy + lz * basis.zy;
            double wz = lx * basis.xz + ly * basis.yz + lz * basis.zz;

            // The point of the plane of the polygon in that direction
            Point3f reference = ctx.p();
            Point3f c0 = Corner(0);
            double nx = planeNormal.x, ny = planeNormal.y, nz = planeNormal.z;
            double denominator = wx * nx + wy * ny + wz * nz;
            if (denominator == 0)
            {
                return default;
            }
            double rx = (double)c0.x - reference.x, ry = (double)c0.y - reference.y, rz = (double)c0.z - reference.z;
            double t = (rx * nx + ry * ny + rz * nz) / denominator;
            if (!(t > 0))
            {
                return default;
            }
            int fan;
            double b0, b1, b2;
            FanWeights(reference.x + t * wx, reference.y + t * wy, reference.z + t * wz, out fan, out b0, out b1, out b2);
            return SampleAtFan(fan, b0, b1, b2, ctx.time, true, (Float)(Math.Abs(lz) / total));
        }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            Quad.Basis basis;
            ProjectedSolidAnglePolygon upperPart, lowerPart;
            double upperArea, lowerArea;
            if (PrepareProjected(ctx, out basis, out upperPart, out lowerPart, out upperArea, out lowerArea))
            {
                return SampleProjected(ctx, u, basis, upperPart, lowerPart, upperArea, lowerArea);
            }

            Float omega;
            if (!SamplesBySolidAngle(ctx, out omega))
            {
                // Sample by area and convert the density to solid angle
                pstd.optional<ShapeSample> ss = Sample(u);
                ss.Ref().intr.time = ctx.time;
                Vector3f wi = ss.value().intr.p() - ctx.p();
                if (pbrt.LengthSquared(wi) == 0)
                {
                    return default;
                }
                wi = pbrt.Normalize(wi);
                ss.Ref().pdf /= pbrt.AbsDot(ss.value().intr.n, -wi) / pbrt.DistanceSquared(ctx.p(), ss.value().intr.p());
                if (pbrt.IsInf(ss.value().pdf))
                {
                    return default;
                }
                return ss;
            }

            // One of the spherical triangles of the fan, chosen by solid angle
            Point3f reference = ctx.p();
            Float target = u[0] * omega;
            int fan = 0;
            Float cumulative = omegaScratch[0];
            while (fan < cornerCount - 3 && target >= cumulative)
            {
                ++fan;
                cumulative += omegaScratch[fan];
            }
            Float low = cumulative - omegaScratch[fan];
            Point2f uTriangle = new Point2f(std.min((target - low) / omegaScratch[fan], (Float)pbrt.OneMinusEpsilon), u[1]);
            Float trianglePDF;
            pstd.array3<Float> bary = pbrt.SampleSphericalTriangle(new pstd.array3<Point3f>(Corner(0), Corner(fan + 1), Corner(fan + 2)), reference, uTriangle,
                                                                   &trianglePDF);
            if (trianglePDF == 0)
            {
                return default;
            }
            return SampleAtFan(fan, bary[0], bary[1], bary[2], ctx.time, true, (omegaScratch[fan] / omega) * trianglePDF);
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            Ray ray = ctx.SpawnRay(wi);
            FanHit hit;
            if (!IntersectFan(ray, Infinity, out hit))
            {
                return 0;
            }
            Quad.Basis basis;
            ProjectedSolidAnglePolygon upperPart, lowerPart;
            double upperArea, lowerArea;
            if (PrepareProjected(ctx, out basis, out upperPart, out lowerPart, out upperArea, out lowerArea))
            {
                double cosine = Math.Abs((double)wi.x * basis.zx + (double)wi.y * basis.zy + (double)wi.z * basis.zz);
                return (Float)(cosine / (upperArea + lowerArea));
            }
            Float omega;
            if (SamplesBySolidAngle(ctx, out omega))
            {
                return 1 / omega;
            }

            // The density of area sampling, in solid angle
            Point3f p0 = Corner(hit.fan + 1);
            Point3f pHit = hit.fan >= 0 ? Corner(0) : p0;
            Point3f p1 = Corner(hit.fan + 1);
            Point3f p2 = Corner(hit.fan + 2);
            pHit = (Float)hit.w0 * Corner(0) + (Float)hit.w1 * p1 + (Float)hit.w2 * p2;
            Float areaPdf = (1 / Area()) / (pbrt.AbsDot((Normal3f)planeNormal, -wi) / pbrt.DistanceSquared(ctx.p(), pHit));
            return pbrt.IsInf(areaPdf) ? 0 : areaPdf;
        }

        // The polygon of three or more corners as triangles (ear clipping in the plane of the polygon). The triangles are given as corner indices, in the
        // winding of the polygon. A polygon that is not simple, or degenerate, gets a fan for what is left.
        private static List<int> EarClip(Point3f[] r)
        {
            int n = r.Length;
            List<int> triangles = new List<int>();

            // Newell's normal and the two axes the polygon is flattened to
            double nx = 0, ny = 0, nz = 0;
            for (int i = 0; i < n; ++i)
            {
                Point3f a = r[i];
                Point3f b = r[(i + 1) % n];
                nx += ((double)a.y - b.y) * ((double)a.z + b.z);
                ny += ((double)a.z - b.z) * ((double)a.x + b.x);
                nz += ((double)a.x - b.x) * ((double)a.y + b.y);
            }
            int drop = Math.Abs(nx) >= Math.Abs(ny) && Math.Abs(nx) >= Math.Abs(nz) ? 0 : (Math.Abs(ny) >= Math.Abs(nz) ? 1 : 2);
            double[] u = new double[n];
            double[] v = new double[n];
            for (int i = 0; i < n; ++i)
            {
                double x = r[i].x, y = r[i].y, z = r[i].z;
                u[i] = drop == 0 ? y : (drop == 1 ? z : x);
                v[i] = drop == 0 ? z : (drop == 1 ? x : y);
            }
            double signedArea = 0;
            for (int i = 0; i < n; ++i)
            {
                int j = (i + 1) % n;
                signedArea += u[i] * v[j] - u[j] * v[i];
            }
            bool reversed = signedArea < 0;
            List<int> remaining = new List<int>();
            for (int i = 0; i < n; ++i)
            {
                remaining.Add(reversed ? n - 1 - i : i);
            }

            while (remaining.Count > 3)
            {
                bool found = false;
                for (int i = 0; i < remaining.Count && !found; ++i)
                {
                    int a = remaining[(i + remaining.Count - 1) % remaining.Count];
                    int b = remaining[i];
                    int c = remaining[(i + 1) % remaining.Count];
                    double turn = (u[b] - u[a]) * (v[c] - v[a]) - (v[b] - v[a]) * (u[c] - u[a]);
                    if (turn <= 0)
                    {
                        continue;
                    }
                    bool empty = true;
                    for (int k = 0; k < remaining.Count && empty; ++k)
                    {
                        int q = remaining[k];
                        if (q == a || q == b || q == c)
                        {
                            continue;
                        }
                        double d0 = (u[b] - u[a]) * (v[q] - v[a]) - (v[b] - v[a]) * (u[q] - u[a]);
                        double d1 = (u[c] - u[b]) * (v[q] - v[b]) - (v[c] - v[b]) * (u[q] - u[b]);
                        double d2 = (u[a] - u[c]) * (v[q] - v[c]) - (v[a] - v[c]) * (u[q] - u[c]);
                        if (d0 >= 0 && d1 >= 0 && d2 >= 0)
                        {
                            empty = false;
                        }
                    }
                    if (empty)
                    {
                        AddTriangle(triangles, a, b, c, reversed);
                        remaining.RemoveAt(i);
                        found = true;
                    }
                }
                if (!found)
                {
                    // no ear (a degenerate polygon): a fan for the rest
                    for (int i = 1; i + 1 < remaining.Count; ++i)
                    {
                        AddTriangle(triangles, remaining[0], remaining[i], remaining[i + 1], reversed);
                    }
                    return triangles;
                }
            }
            AddTriangle(triangles, remaining[0], remaining[1], remaining[2], reversed);
            return triangles;
        }

        private static void AddTriangle(List<int> triangles, int a, int b, int c, bool reversed)
        {
            triangles.Add(reversed ? c : a);
            triangles.Add(b);
            triangles.Add(reversed ? a : c);
        }

        // The Ngon shapes (and the Triangle shapes of the polygons that are not planar and convex) of a mesh of polygons. counts holds the corner count of each
        // polygon and indices the corners of all of them, in order around each. P is in object space. faceIndices has one entry per polygon (or none).
        public static pstd.vector<Shape> CreateShapes(Transform renderFromObject, bool reverseOrientation, List<int> counts, List<int> indices, List<Point3f> P,
                                                      List<Normal3f> N, List<Point2f> uv, List<int> faceIndices, bool projectedSampling, Allocator alloc)
        {
            List<Point3f> fanP = new List<Point3f>();
            List<Normal3f> fanN = new List<Normal3f>();
            List<Point2f> fanUV = new List<Point2f>();
            List<int> fanIndices = new List<int>();
            List<int> fanFaces = new List<int>();
            List<int[]> polygons = new List<int[]>();
            List<int[]> polygonSampleCorners = new List<int[]>();
            List<int> triangleIndices = new List<int>();
            List<int> triangleFaces = new List<int>();

            int offset = 0;
            for (int f = 0; f < counts.Count; ++f)
            {
                int n = counts[f];
                int face = faceIndices.Count != 0 ? faceIndices[f] : 0;
                if (n < 3)
                {
                    offset += n;
                    continue;
                }
                int[] corner = new int[n];
                Point3f[] r = new Point3f[n];
                double[][] c = new double[n][];
                for (int k = 0; k < n; ++k)
                {
                    corner[k] = indices[offset + k];
                    r[k] = renderFromObject.Apply(P[corner[k]]);
                    c[k] = new double[] { r[k].x, r[k].y, r[k].z };
                }
                offset += n;

                int[] kept;
                if (Classify(c, out kept))
                {
                    int firstVertex = fanP.Count;
                    // The coordinates of the polygon in its plane, scaled to its bounding box, for a mesh without texture coordinates
                    Point2f[] planar = null;
                    if (uv.Count == 0)
                    {
                        planar = PlanarCoordinates(c);
                    }
                    for (int k = 0; k < n; ++k)
                    {
                        fanP.Add(P[corner[k]]);
                        if (N.Count != 0)
                        {
                            fanN.Add(N[corner[k]]);
                        }
                        fanUV.Add(uv.Count != 0 ? uv[corner[k]] : planar[k]);
                    }
                    int firstTriangle = fanIndices.Count / 3;
                    for (int j = 0; j < n - 2; ++j)
                    {
                        fanIndices.Add(firstVertex);
                        fanIndices.Add(firstVertex + j + 1);
                        fanIndices.Add(firstVertex + j + 2);
                        fanFaces.Add(face);
                    }
                    polygons.Add(new int[] { firstVertex, firstTriangle, n });
                    polygonSampleCorners.Add(kept);
                }
                else
                {
                    List<int> ears = EarClip(r);
                    for (int i = 0; i < ears.Count; ++i)
                    {
                        triangleIndices.Add(corner[ears[i]]);
                    }
                    for (int i = 0; i < ears.Count / 3; ++i)
                    {
                        triangleFaces.Add(face);
                    }
                }
            }

            pstd.vector<Shape> shapes = new pstd.vector<Shape>();
            if (triangleIndices.Count != 0)
            {
                TriangleMesh triangleMesh = new TriangleMesh(renderFromObject, reverseOrientation, new pstd.vector<int>(triangleIndices), new pstd.vector<Point3f>(P),
                                                             new pstd.vector<Vector3f>(), new pstd.vector<Normal3f>(N), new pstd.vector<Point2f>(uv),
                                                             new pstd.vector<int>(faceIndices.Count != 0 ? triangleFaces : new List<int>()), alloc);
                pstd.vector<Shape> triangles = Triangle.CreateTriangles(triangleMesh, alloc);
                for (int i = 0; i < (int)triangles.size(); ++i)
                {
                    shapes.push_back(triangles[i]);
                }
            }
            if (polygons.Count != 0)
            {
                TriangleMesh fanMesh = new TriangleMesh(renderFromObject, reverseOrientation, new pstd.vector<int>(fanIndices), new pstd.vector<Point3f>(fanP),
                                                        new pstd.vector<Vector3f>(), new pstd.vector<Normal3f>(fanN), new pstd.vector<Point2f>(fanUV),
                                                        new pstd.vector<int>(faceIndices.Count != 0 ? fanFaces : new List<int>()), alloc);
                for (int i = 0; i < polygons.Count; ++i)
                {
                    shapes.push_back(new Ngon(fanMesh, polygons[i][0], polygons[i][1], polygons[i][2], polygonSampleCorners[i], projectedSampling));
                }
            }
            return shapes;
        }

        // The coordinates of the corners in the plane of the polygon (the axes: the first edge, and the normal times it), each scaled to [0, 1] over the polygon
        private static Point2f[] PlanarCoordinates(double[][] c)
        {
            int n = c.Length;
            double[] center = new double[3];
            for (int i = 0; i < n; ++i)
            {
                for (int k = 0; k < 3; ++k)
                {
                    center[k] += c[i][k] / n;
                }
            }
            double[] normal = new double[3];
            for (int i = 0; i < n; ++i)
            {
                double[] cross = Cross(Sub(c[i], center), Sub(c[(i + 1) % n], center));
                for (int k = 0; k < 3; ++k)
                {
                    normal[k] += cross[k];
                }
            }
            double normalLength = Length(normal);
            double[] axisU = Sub(c[1], c[0]);
            double axisULength = Length(axisU);
            double[] unitU = { axisU[0] / axisULength, axisU[1] / axisULength, axisU[2] / axisULength };
            double[] unitNormal = { normal[0] / normalLength, normal[1] / normalLength, normal[2] / normalLength };
            double[] unitV = Cross(unitNormal, unitU);
            double[] pu = new double[n];
            double[] pv = new double[n];
            double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
            for (int i = 0; i < n; ++i)
            {
                double[] d = Sub(c[i], c[0]);
                pu[i] = Dot(d, unitU);
                pv[i] = Dot(d, unitV);
                minU = Math.Min(minU, pu[i]);
                maxU = Math.Max(maxU, pu[i]);
                minV = Math.Min(minV, pv[i]);
                maxV = Math.Max(maxV, pv[i]);
            }
            Point2f[] result = new Point2f[n];
            for (int i = 0; i < n; ++i)
            {
                result[i] = new Point2f((Float)((pu[i] - minU) / Math.Max(maxU - minU, 1e-30)), (Float)((pv[i] - minV) / Math.Max(maxV - minV, 1e-30)));
            }
            return result;
        }

        // "sampling": "projected" (the default) or "solidangle"
        public static bool ParseSampling(string sampling, FileLoc loc)
        {
            if (sampling != "solidangle" && sampling != "projected")
            {
                pbrt.ErrorExit(loc, "\"sampling\" must be \"solidangle\" or \"projected\".");
            }
            return sampling == "projected";
        }

        // Shape "ngonmesh": "integer counts" (the corner count of each polygon; none for a single polygon of all the points), "integer indices" (the corners of
        // all the polygons, in order around each), "point3 P", "point2 uv", "normal N", "integer faceIndices" (one per polygon), "string sampling"
        public static pstd.vector<Shape> Create(Transform renderFromObject, bool reverseOrientation, ParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            List<int> counts = parameters.GetIntArray("counts");
            List<int> indices = parameters.GetIntArray("indices");
            List<Point3f> P = parameters.GetPoint3fArray("P");
            List<Point2f> uv = parameters.GetPoint2fArray("uv");
            if (P.Count == 0)
            {
                pbrt.ErrorExit(loc, "Vertex positions \"P\" must be provided with ngonmesh shape.");
            }
            if (counts.Count == 0)
            {
                if (indices.Count != 0)
                {
                    pbrt.ErrorExit(loc, "\"counts\" (the corner count of each polygon) must be provided with \"indices\" for the ngonmesh shape.");
                }
                counts = new List<int> { P.Count };
                for (int i = 0; i < P.Count; ++i)
                {
                    indices.Add(i);
                }
            }
            int total = 0;
            for (int i = 0; i < counts.Count; ++i)
            {
                if (counts[i] < 3)
                {
                    pbrt.ErrorExit(loc, pbrt.StringPrintf("ngonmesh polygon %d has %d corners (at least 3 are needed).", i, counts[i]));
                }
                total += counts[i];
            }
            if (total != indices.Count)
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("ngonmesh \"counts\" add up to %d corners but %d \"indices\" were given.", total, indices.Count));
            }
            for (int i = 0; i < indices.Count; ++i)
            {
                if (indices[i] < 0 || indices[i] >= P.Count)
                {
                    pbrt.ErrorExit(loc, pbrt.StringPrintf("ngonmesh has out of-bounds vertex index %d (%d \"P\" values were given).", indices[i], P.Count));
                }
            }
            if (uv.Count != 0 && uv.Count != P.Count)
            {
                pbrt.Error(loc, "Number of \"uv\"s for ngon mesh must match \"P\"s. Discarding uvs.");
                uv = new List<Point2f>();
            }
            List<Normal3f> N = parameters.GetNormal3fArray("N");
            if (N.Count != 0 && N.Count != P.Count)
            {
                pbrt.Error(loc, "Number of \"N\"s for ngon mesh must match \"P\"s. Discarding \"N\"s.");
                N = new List<Normal3f>();
            }
            List<int> faceIndices = parameters.GetIntArray("faceIndices");
            if (faceIndices.Count != 0 && faceIndices.Count != counts.Count)
            {
                pbrt.Error(loc, pbrt.StringPrintf("Number of face indices %d does not match number of polygons %d. Discarding face indices.", faceIndices.Count,
                                                  counts.Count));
                faceIndices = new List<int>();
            }
            return CreateShapes(renderFromObject, reverseOrientation, counts, indices, P, N, uv, faceIndices,
                                ParseSampling(parameters.GetOneString("sampling", "projected"), loc), alloc);
        }
    }
}  // namespace pbrt
