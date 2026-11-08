// (not in pbrt-v4) shapes_polygon_common.cs
//
// What the planar convex polygon shapes of the second form (Quad2, Ngon2 and Rectangle2) have in common when they are sampled from a reference point: the
// test of whether the reference point is in the plane of the shape, the parts of the shape above and below the horizon of the reference point for the
// sampling by projected solid angle (polygon_sampling.cs, Peters 2021) and the sample of the direction in that sampling, and the conversion of a sample by
// area to the density in solid angle. A shape gives its corners (in order around it); what it does with the point of the plane that is sampled (finding the
// triangle of the fan, or the (u,v) of the bilinear patch) is its own.

using System;

namespace pbrt
{
    internal static class PolygonSampling
    {
        // The most corners of a polygon that is sampled by projected solid angle
        public const int MaxCornerCount = 11;

        // The sampling by projected solid angle falls back to the sampling by area when the projected solid angle of the shape is smaller than this
        public const double MinProjectedSampleArea = 1e-8;

        private const double BelowOne = 1 - 1.1102230246251565e-16;

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
        private static Point3f[] cornerScratch;
        [ThreadStatic]
        private static Float[] omegaScratch;

        private const Float MinSphericalSampleArea = 3e-4f;
        private const Float MaxSphericalSampleArea = 6.22f;

        // Whether the reference point is in the plane of the polygon (within its error bounds)
        public static bool ReferencePointOnPlane(ShapeSampleContext ctx, Point3f corner, Vector3f planeNormal)
        {
            const Float rounding = 4 * pbrt.MachineEpsilon;
            Vector3f toReference = ctx.p() - corner;
            Vector3f absoluteNormal = pbrt.Abs(planeNormal);
            Float bound = pbrt.Dot(absoluteNormal, ctx.pi.Error() + rounding * pbrt.Abs(toReference));
            return std.abs(pbrt.Dot(planeNormal, toReference)) <= bound;
        }

        // The parts of the polygon above and below the horizon of the reference point (the plane through it with the shading normal), as polygons for the
        // sampling by projected solid angle, and their projected solid angles. corners are the first count corners, in order around the polygon.
        // False if the projected solid angle is too small to sample.
        public static bool PrepareProjected(ShapeSampleContext ctx, Point3f[] corners, int count, out Quad.Basis basis, out ProjectedSolidAnglePolygon upper,
                                            out ProjectedSolidAnglePolygon lower, out double upperArea, out double lowerArea)
        {
            upperArea = 0;
            lowerArea = 0;
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
            for (int i = 0; i < count; ++i)
            {
                Point3f c = corners[i];
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

        // The direction sampled in proportion to the projected solid angle, and the point of the plane of the polygon (through corner, with the normal n, which
        // need not have unit length) in that direction, and the density of the direction in solid angle. False if the direction does not meet the plane in front
        // of the reference point.
        public static bool SampleProjected(ShapeSampleContext ctx, Point2f u, Quad.Basis basis, ProjectedSolidAnglePolygon upper, ProjectedSolidAnglePolygon lower,
                                           double upperArea, double lowerArea, Point3f corner, double nx, double ny, double nz, out double px, out double py,
                                           out double pz, out Float pdf)
        {
            px = 0;
            py = 0;
            pz = 0;
            pdf = 0;
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
            double lx, ly, lz;
            chosen.Sample(Math.Min(u0, BelowOne), Math.Min((double)u[1], BelowOne), out lx, out ly, out lz);
            lz *= sign;
            double wx = lx * basis.xx + ly * basis.yx + lz * basis.zx;
            double wy = lx * basis.xy + ly * basis.yy + lz * basis.zy;
            double wz = lx * basis.xz + ly * basis.yz + lz * basis.zz;

            // The point of the plane of the polygon in that direction
            Point3f reference = ctx.p();
            double denominator = wx * nx + wy * ny + wz * nz;
            if (denominator == 0)
            {
                return false;
            }
            double rx = (double)corner.x - reference.x, ry = (double)corner.y - reference.y, rz = (double)corner.z - reference.z;
            double t = (rx * nx + ry * ny + rz * nz) / denominator;
            if (!(t > 0))
            {
                return false;
            }
            px = reference.x + t * wx;
            py = reference.y + t * wy;
            pz = reference.z + t * wz;
            pdf = (Float)(Math.Abs(lz) / total);
            return true;
        }

        // The area of the polygon whose corners are the contiguous vertices firstVertex... of p, the cumulative area of the triangles of its fan (v0, v[j + 1],
        // v[j + 2]) as fractions of it, and the normal of its plane, in double precision
        public static void FanGeometry(Point3f[] p, int firstVertex, int cornerCount, out Float area, out Float[] fanAreaCdf, out Vector3f planeNormal)
        {
            Point3f p0 = p[firstVertex];
            fanAreaCdf = new Float[cornerCount - 2];
            double total = 0;
            double nx = 0, ny = 0, nz = 0;
            double[] cumulative = new double[cornerCount - 2];
            for (int j = 0; j < cornerCount - 2; ++j)
            {
                Point3f b = p[firstVertex + j + 1];
                Point3f c = p[firstVertex + j + 2];
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

        // The corner at position k of a polygon whose corners are p[indices[first + k]] (or the contiguous vertices first + k of p if indices is null)
        private static Point3f FanCorner(Point3f[] p, int[] indices, int first, int k)
        {
            return p[indices != null ? indices[first + k] : first + k];
        }

        private static void ToRayCoordinates(Point3f c, Point3f origin, int kx, int ky, int kz, Float sx, Float sy, Float sz, out Float x, out Float y, out Float z)
        {
            Point3f pt = pbrt.Permute(c - (Vector3f)origin, kx, ky, kz);
            x = pt.x + sx * pt.z;
            y = pt.y + sy * pt.z;
            z = pt.z * sz;
        }

        // The triangle of the fan of the polygon that the ray hits, if any: the first whose edge functions (all in double precision) pass the test. The fan starts
        // at the corner `start` (the triangles are (c[start], c[start + j + 1], c[start + j + 2]), the corners taken around the polygon), so a quad can be
        // split along either of its diagonals. The ray moves to the ray coordinate system once; every diagonal is evaluated once and used with opposite signs
        // for the two triangles it separates, and a side is evaluated from its two projected vertices only, so a ray cannot pass between two triangles of the fan,
        // between two polygons, or between a polygon and a Triangle that share an edge and its two vertices. The weights are those of the triangle that was hit.
        public static bool IntersectFan(in Ray ray, Float tMax, Point3f[] p, int[] indices, int first, int cornerCount, int start, out int fan, out double b0,
                                        out double b1, out double b2, out Float t)
        {
            fan = 0;
            b0 = 0;
            b1 = 0;
            b2 = 0;
            t = 0;
            int kz = pbrt.MaxComponentIndex(pbrt.Abs(ray.d));
            int kx = kz + 1;
            if (kx == 3)
            {
                kx = 0;
            }
            int ky = kx + 1;
            if (ky == 3)
            {
                ky = 0;
            }
            Vector3f d = pbrt.Permute(ray.d, kx, ky, kz);
            Float sx = -d.x / d.z;
            Float sy = -d.y / d.z;
            Float sz = 1 / d.z;
            Float x0, y0, z0, x1, y1, z1, x2, y2, z2;
            int k = start;
            ToRayCoordinates(FanCorner(p, indices, first, k), ray.o, kx, ky, kz, sx, sy, sz, out x0, out y0, out z0);
            ++k;
            if (k == cornerCount)
            {
                k = 0;
            }
            ToRayCoordinates(FanCorner(p, indices, first, k), ray.o, kx, ky, kz, sx, sy, sz, out x1, out y1, out z1);
            // The diagonal from the first corner to the corner that starts the triangle: evaluated once, and used with the opposite sign by the triangle that ends at it
            double diagonalFirst = Quad.EdgeFunction(x0, y0, x1, y1);
            for (int j = 0; j < cornerCount - 2; ++j)
            {
                ++k;
                if (k == cornerCount)
                {
                    k = 0;
                }
                ToRayCoordinates(FanCorner(p, indices, first, k), ray.o, kx, ky, kz, sx, sy, sz, out x2, out y2, out z2);
                double diagonalSecond = Quad.EdgeFunction(x0, y0, x2, y2);
                double side = Quad.EdgeFunction(x1, y1, x2, y2);
                if (Quad.TestTriangle(side, -diagonalSecond, diagonalFirst, x0, y0, z0, x1, y1, z1, x2, y2, z2, tMax, out b0, out b1, out b2, out t))
                {
                    fan = j;
                    return true;
                }
                x1 = x2;
                y1 = y2;
                z1 = z2;
                diagonalFirst = diagonalSecond;
            }
            return false;
        }

        // IntersectFan for a polygon of four corners, written out: the triangles are (c0, c1, c2) and (c0, c2, c3), split along the diagonal c0-c2, which is
        // evaluated once and used with opposite signs by the two
        public static bool IntersectFan4(in Ray ray, Float tMax, in Point3f c0, in Point3f c1, in Point3f c2, in Point3f c3, out int fan, out double b0, out double b1,
                                         out double b2, out Float t)
        {
            fan = 0;
            int kz = pbrt.MaxComponentIndex(pbrt.Abs(ray.d));
            int kx = kz + 1;
            if (kx == 3)
            {
                kx = 0;
            }
            int ky = kx + 1;
            if (ky == 3)
            {
                ky = 0;
            }
            Vector3f d = pbrt.Permute(ray.d, kx, ky, kz);
            Float sx = -d.x / d.z;
            Float sy = -d.y / d.z;
            Float sz = 1 / d.z;
            ToRayCoordinates(c0, ray.o, kx, ky, kz, sx, sy, sz, out Float x0, out Float y0, out Float z0);
            ToRayCoordinates(c1, ray.o, kx, ky, kz, sx, sy, sz, out Float x1, out Float y1, out Float z1);
            ToRayCoordinates(c2, ray.o, kx, ky, kz, sx, sy, sz, out Float x2, out Float y2, out Float z2);
            ToRayCoordinates(c3, ray.o, kx, ky, kz, sx, sy, sz, out Float x3, out Float y3, out Float z3);
            double diagonal01 = Quad.EdgeFunction(x0, y0, x1, y1);
            double diagonal02 = Quad.EdgeFunction(x0, y0, x2, y2);
            double diagonal03 = Quad.EdgeFunction(x0, y0, x3, y3);
            if (Quad.TestTriangle(Quad.EdgeFunction(x1, y1, x2, y2), -diagonal02, diagonal01, x0, y0, z0, x1, y1, z1, x2, y2, z2, tMax, out b0, out b1, out b2, out t))
            {
                return true;
            }
            fan = 1;
            return Quad.TestTriangle(Quad.EdgeFunction(x2, y2, x3, y3), -diagonal03, diagonal02, x0, y0, z0, x2, y2, z2, x3, y3, z3, tMax, out b0, out b1, out b2, out t);
        }

        // The bounds of the polygon whose corners are the contiguous vertices firstVertex... of p
        public static Bounds3f FanBounds(Point3f[] p, int firstVertex, int cornerCount)
        {
            Point3f first = p[firstVertex];
            Bounds3f bounds = new Bounds3f(first, first);
            for (int k = 1; k < cornerCount; ++k)
            {
                Point3f c = p[firstVertex + k];
                bounds = pbrt.Union(bounds, new Bounds3f(c, c));
            }
            return bounds;
        }

        // The cone of the normals of the polygon with the corners firstVertex... of the fan mesh
        public static DirectionCone FanNormalBounds(TriangleMesh mesh, int firstVertex, int cornerCount, Vector3f planeNormal)
        {
            Normal3f n = (Normal3f)planeNormal;
            if (mesh.n != null)
            {
                Normal3f ns = mesh.n[firstVertex];
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

        // The weights, in the triangle of the fan that holds the point of the plane of the polygon, of the corners of that triangle
        public static void FanWeights(Point3f[] p, int firstVertex, int cornerCount, double px, double py, double pz, out int fan, out double b0, out double b1,
                                      out double b2)
        {
            fan = 0;
            b0 = 1;
            b1 = 0;
            b2 = 0;
            double bestScore = double.NegativeInfinity;
            Point3f a = p[firstVertex];
            for (int j = 0; j < cornerCount - 2; ++j)
            {
                Point3f b = p[firstVertex + j + 1];
                Point3f c = p[firstVertex + j + 2];
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

        // The sample of the point of the triangle `fan` of the polygon with the weights b of its corners (v0, v[fan + 1], v[fan + 2])
        public static ShapeSample SampleAtFan(TriangleMesh mesh, int firstVertex, Vector3f planeNormal, int fan, double b0, double b1, double b2, Float time,
                                              bool hasTime, Float pdf)
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

        // A point of the polygon chosen uniformly by area: a triangle of the fan, by area, then a uniform point of it (density 1 / area)
        public static ShapeSample SampleFan(TriangleMesh mesh, int firstVertex, int cornerCount, Float[] fanAreaCdf, Float area, Vector3f planeNormal, Point2f u)
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
            return SampleAtFan(mesh, firstVertex, planeNormal, fan, b[0], b[1], b[2], 0, false, 1 / area);
        }

        // The solid angles of the triangles of the fan seen from the reference point (in omegaScratch), their sum, and whether the polygon is sampled by
        // solid angle (as spherical triangles)
        private static bool SamplesBySolidAngle(Point3f[] p, int firstVertex, int cornerCount, Vector3f planeNormal, ShapeSampleContext ctx, out Float total)
        {
            total = 0;
            if (omegaScratch == null)
            {
                omegaScratch = new Float[MaxCornerCount];
            }
            Point3f reference = ctx.p();
            Vector3f first = pbrt.Normalize(p[firstVertex] - reference);
            Vector3f previous = pbrt.Normalize(p[firstVertex + 1] - reference);
            for (int j = 0; j < cornerCount - 2; ++j)
            {
                Vector3f next = pbrt.Normalize(p[firstVertex + j + 2] - reference);
                Float omega = pbrt.SphericalTriangleArea(first, previous, next);
                if (omega < MinSphericalSampleArea || omega > MaxSphericalSampleArea)
                {
                    return false;
                }
                omegaScratch[j] = omega;
                total += omega;
                previous = next;
            }
            return !ReferencePointOnPlane(ctx, p[firstVertex], planeNormal);
        }

        // The parts of the polygon above and below the horizon of the reference point for the sampling by projected solid angle (with the corners that it uses,
        // sampleCorners, or all of them if that is null), and their projected solid angles. False if the polygon is not sampled that way from the reference point.
        private static bool PrepareProjectedFan(Point3f[] p, int firstVertex, int cornerCount, Vector3f planeNormal, int[] sampleCorners, bool projectedSampling,
                                                ShapeSampleContext ctx, out Quad.Basis basis, out ProjectedSolidAnglePolygon upper,
                                                out ProjectedSolidAnglePolygon lower, out double upperArea, out double lowerArea)
        {
            basis = default;
            upper = null;
            lower = null;
            upperArea = 0;
            lowerArea = 0;
            if (!projectedSampling || ctx.ns == new Normal3f(0, 0, 0) || ReferencePointOnPlane(ctx, p[firstVertex], planeNormal))
            {
                return false;
            }
            if (cornerScratch == null)
            {
                cornerScratch = new Point3f[MaxCornerCount];
            }
            int count = sampleCorners != null ? sampleCorners.Length : cornerCount;
            for (int i = 0; i < count; ++i)
            {
                cornerScratch[i] = p[firstVertex + (sampleCorners != null ? sampleCorners[i] : i)];
            }
            return PrepareProjected(ctx, cornerScratch, count, out basis, out upper, out lower, out upperArea, out lowerArea);
        }

        // A sample of the polygon (of the fan mesh) from the reference point, with its density in solid angle: proportional to the projected solid angle if
        // projectedSampling is set and the reference point has a shading normal, else one of the spherical triangles of the fan, chosen by solid angle; a polygon
        // of a small or large solid angle, and a reference point in its plane, are sampled by area.
        public static pstd.optional<ShapeSample> SampleFromPoint(TriangleMesh mesh, int firstVertex, int cornerCount, Float[] fanAreaCdf, Float area,
                                                                 Vector3f planeNormal, int[] sampleCorners, bool projectedSampling, ShapeSampleContext ctx, Point2f u)
        {
            if (PrepareProjectedFan(mesh.p, firstVertex, cornerCount, planeNormal, sampleCorners, projectedSampling, ctx, out Quad.Basis basis,
                                    out ProjectedSolidAnglePolygon upperPart, out ProjectedSolidAnglePolygon lowerPart, out double upperArea, out double lowerArea))
            {
                if (!SampleProjected(ctx, u, basis, upperPart, lowerPart, upperArea, lowerArea, mesh.p[firstVertex], planeNormal.x, planeNormal.y, planeNormal.z,
                                     out double px, out double py, out double pz, out Float pdf))
                {
                    return default;
                }
                FanWeights(mesh.p, firstVertex, cornerCount, px, py, pz, out int fan, out double b0, out double b1, out double b2);
                return SampleAtFan(mesh, firstVertex, planeNormal, fan, b0, b1, b2, ctx.time, true, pdf);
            }

            if (!SamplesBySolidAngle(mesh.p, firstVertex, cornerCount, planeNormal, ctx, out Float omega))
            {
                // Sample by area and convert the density to solid angle
                return AreaSampleToSolidAngle(ctx, SampleFan(mesh, firstVertex, cornerCount, fanAreaCdf, area, planeNormal, u));
            }

            // One of the spherical triangles of the fan, chosen by solid angle
            Float target = u[0] * omega;
            int chosen = 0;
            Float cumulative = omegaScratch[0];
            while (chosen < cornerCount - 3 && target >= cumulative)
            {
                ++chosen;
                cumulative += omegaScratch[chosen];
            }
            Float low = cumulative - omegaScratch[chosen];
            Point2f uTriangle = new Point2f(std.min((target - low) / omegaScratch[chosen], (Float)pbrt.OneMinusEpsilon), u[1]);
            Float trianglePDF;
            pstd.array3<Float> bary;
            unsafe
            {
                bary = pbrt.SampleSphericalTriangle(new pstd.array3<Point3f>(mesh.p[firstVertex], mesh.p[firstVertex + chosen + 1], mesh.p[firstVertex + chosen + 2]),
                                                    ctx.p(), uTriangle, &trianglePDF);
            }
            if (trianglePDF == 0)
            {
                return default;
            }
            return SampleAtFan(mesh, firstVertex, planeNormal, chosen, bary[0], bary[1], bary[2], ctx.time, true, (omegaScratch[chosen] / omega) * trianglePDF);
        }

        // The density of SampleFromPoint for the direction wi
        public static Float PdfFromPoint(TriangleMesh mesh, int firstVertex, int cornerCount, Float area, Vector3f planeNormal, int[] sampleCorners,
                                         bool projectedSampling, ShapeSampleContext ctx, Vector3f wi)
        {
            Ray ray = ctx.SpawnRay(wi);
            if (!IntersectFan(ray, Infinity, mesh.p, null, firstVertex, cornerCount, 0, out int fan, out double b0, out double b1, out double b2, out Float t))
            {
                return 0;
            }
            if (PrepareProjectedFan(mesh.p, firstVertex, cornerCount, planeNormal, sampleCorners, projectedSampling, ctx, out Quad.Basis basis,
                                    out ProjectedSolidAnglePolygon upperPart, out ProjectedSolidAnglePolygon lowerPart, out double upperArea, out double lowerArea))
            {
                return ProjectedPdf(basis, wi, upperArea, lowerArea);
            }
            if (SamplesBySolidAngle(mesh.p, firstVertex, cornerCount, planeNormal, ctx, out Float omega))
            {
                return 1 / omega;
            }

            // The density of area sampling, in solid angle
            Point3f pHit = (Float)b0 * mesh.p[firstVertex] + (Float)b1 * mesh.p[firstVertex + fan + 1] + (Float)b2 * mesh.p[firstVertex + fan + 2];
            return AreaPdfToSolidAngle(ctx, area, (Normal3f)planeNormal, wi, pHit);
        }

        // The density of the projected solid angle sampling of the direction wi, whose cosine with the shading normal is that of the basis
        public static Float ProjectedPdf(Quad.Basis basis, Vector3f wi, double upperArea, double lowerArea)
        {
            double cosine = Math.Abs((double)wi.x * basis.zx + (double)wi.y * basis.zy + (double)wi.z * basis.zz);
            return (Float)(cosine / (upperArea + lowerArea));
        }

        // A sample by area (ss, with the density per unit area) as a sample by solid angle from the reference point; none if it is at the reference point
        public static pstd.optional<ShapeSample> AreaSampleToSolidAngle(ShapeSampleContext ctx, pstd.optional<ShapeSample> ss)
        {
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

        // The density of the sampling by area of the point pHit (with the normal n) of a shape of the given area, in solid angle from the reference point
        public static Float AreaPdfToSolidAngle(ShapeSampleContext ctx, Float area, Normal3f n, Vector3f wi, Point3f pHit)
        {
            Float pdf = (1 / area) / (pbrt.AbsDot(n, -wi) / pbrt.DistanceSquared(ctx.p(), pHit));
            return pbrt.IsInf(pdf) ? 0 : pdf;
        }
    }
}  // namespace pbrt
