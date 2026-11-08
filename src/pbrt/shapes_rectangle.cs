// (not in pbrt-v4) shapes_rectangle.cs
//
// Shape "rectanglemesh": rectangles (four corners with right angles, or a parallelogram test that fails: the rectangle is then made an Ngon). A Rectangle intersects
// and is shaded as the Ngon of its four corners (the watertight fan of shapes_ngon.cs), and is sampled by solid angle with the area preserving parametrization of
// the spherical rectangle of Ureña, Fajardo and King, "An Area-Preserving Parametrization for Spherical Rectangles", EGSR 2013: the density is exactly
// 1 / (solid angle of the rectangle), and the sampling is a closed form, with no iteration and no rejection.
//
//  * "string sampling" "solidangle" (the default): the spherical rectangle sampling. A rectangle of a small solid angle, and a reference point in its plane, are
//    sampled by area and the density is converted to solid angle.
//  * "string sampling" "projected": the projected solid angle sampling of polygon_sampling.cs, as for any Ngon.
//
// The corners are given in order around the rectangle: c0, c1 = c0 + a, c2 = c0 + a + b, c3 = c0 + b.

using System;
using System.Collections.Generic;

namespace pbrt
{
    public sealed unsafe class Rectangle : Shape
    {
        private const Float MinSphericalSampleArea = 3e-4f;
        private const double RightAngleTolerance = 1e-5;
        private const double InPlaneTolerance = 1e-7;

        private Ngon polygon;
        private bool projectedSampling;
        private Point3f corner;
        private Vector3f edgeX;
        private Vector3f edgeY;
        private double lengthX;
        private double lengthY;

        // Ureña's spherical rectangle, as seen from a reference point
        internal struct SphericalRectangle
        {
            public double x0, y0, z0, x1, y1;
            public double b0, b1, b0Squared, k, solidAngle;
            public double zx, zy, zz;
        }

        public Rectangle(Ngon polygon, bool projectedSampling)
        {
            this.polygon = polygon;
            this.projectedSampling = projectedSampling;
            corner = polygon.CornerPoint(0);
            Vector3f ex = polygon.CornerPoint(1) - corner;
            Vector3f ey = polygon.CornerPoint(3) - corner;
            lengthX = pbrt.Length(ex);
            lengthY = pbrt.Length(ey);
            edgeX = ex / (Float)lengthX;
            edgeY = ey / (Float)lengthY;
        }

        // Whether the four points (in order) are a rectangle: a parallelogram with a right angle, within a tolerance relative to the size
        public static bool IsRectangle(Point3f[] c)
        {
            double ax = (double)c[1].x - c[0].x, ay = (double)c[1].y - c[0].y, az = (double)c[1].z - c[0].z;
            double bx = (double)c[3].x - c[0].x, by = (double)c[3].y - c[0].y, bz = (double)c[3].z - c[0].z;
            double la = Math.Sqrt(ax * ax + ay * ay + az * az), lb = Math.Sqrt(bx * bx + by * by + bz * bz);
            if (la == 0 || lb == 0)
            {
                return false;
            }
            if (Math.Abs(ax * bx + ay * by + az * bz) > RightAngleTolerance * la * lb)
            {
                return false;
            }
            double scale = Math.Max(la, lb);
            double dx = (double)c[2].x - c[0].x - ax - bx, dy = (double)c[2].y - c[0].y - ay - by, dz = (double)c[2].z - c[0].z - az - bz;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz) <= RightAngleTolerance * scale;
        }

        public override pstd.optional<ShapeIntersection> Intersect(Ray ray, Float tMax = Infinity)
        {
            return polygon.Intersect(ray, tMax);
        }

        public override bool IntersectP(Ray ray, Float tMax = Infinity)
        {
            return polygon.IntersectP(ray, tMax);
        }

        public override Float Area()
        {
            return polygon.Area();
        }

        public override Bounds3f Bounds()
        {
            return polygon.Bounds();
        }

        public override DirectionCone NormalBounds()
        {
            return polygon.NormalBounds();
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ Rectangle %f x %f ]", (Float)lengthX, (Float)lengthY);
        }

        public override Float PDF(Interaction intr)
        {
            return polygon.PDF(intr);
        }

        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            return polygon.Sample(u);
        }

        // The spherical rectangle seen from p; false if p is (nearly) in the plane of the rectangle, or the rectangle is too small to sample that way
        internal static bool Prepare(Point3f corner, Vector3f edgeX, Vector3f edgeY, double lengthX, double lengthY, Point3f p, out SphericalRectangle sr)
        {
            sr = default;
            double dx = (double)corner.x - p.x, dy = (double)corner.y - p.y, dz = (double)corner.z - p.z;
            double xx = edgeX.x, xy = edgeX.y, xz = edgeX.z;
            double yx = edgeY.x, yy = edgeY.y, yz = edgeY.z;
            double zx = xy * yz - xz * yy, zy = xz * yx - xx * yz, zz = xx * yy - xy * yx;
            double x0 = dx * xx + dy * xy + dz * xz;
            double y0 = dx * yx + dy * yy + dz * yz;
            double z0 = dx * zx + dy * zy + dz * zz;
            double size = Math.Max(lengthX, lengthY);
            if (Math.Abs(z0) <= InPlaneTolerance * (size + Math.Abs(x0) + Math.Abs(y0)))
            {
                return false;
            }
            // the frame is flipped so that the rectangle is in front of the reference point (z0 < 0)
            if (z0 > 0)
            {
                z0 = -z0;
                zx = -zx;
                zy = -zy;
                zz = -zz;
            }
            double x1 = x0 + lengthX, y1 = y0 + lengthY;
            double[] v00 = { x0, y0, z0 }, v01 = { x0, y1, z0 }, v10 = { x1, y0, z0 }, v11 = { x1, y1, z0 };
            double[] n0 = NormalizedCross(v00, v10), n1 = NormalizedCross(v10, v11), n2 = NormalizedCross(v11, v01), n3 = NormalizedCross(v01, v00);
            double g0 = Math.Acos(Math.Max(-1, Math.Min(1, -Dot(n0, n1))));
            double g1 = Math.Acos(Math.Max(-1, Math.Min(1, -Dot(n1, n2))));
            double g2 = Math.Acos(Math.Max(-1, Math.Min(1, -Dot(n2, n3))));
            double g3 = Math.Acos(Math.Max(-1, Math.Min(1, -Dot(n3, n0))));
            sr.solidAngle = g0 + g1 + g2 + g3 - 2 * Math.PI;
            if (!(sr.solidAngle >= MinSphericalSampleArea))
            {
                return false;
            }
            sr.x0 = x0;
            sr.y0 = y0;
            sr.z0 = z0;
            sr.x1 = x1;
            sr.y1 = y1;
            sr.b0 = n0[2];
            sr.b1 = n2[2];
            sr.b0Squared = sr.b0 * sr.b0;
            sr.k = 2 * Math.PI - g2 - g3;
            sr.zx = zx;
            sr.zy = zy;
            sr.zz = zz;
            return true;
        }

        private static double Dot(double[] a, double[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }

        private static double[] NormalizedCross(double[] a, double[] b)
        {
            double cx = a[1] * b[2] - a[2] * b[1], cy = a[2] * b[0] - a[0] * b[2], cz = a[0] * b[1] - a[1] * b[0];
            double length = Math.Sqrt(cx * cx + cy * cy + cz * cz);
            return new double[] { cx / length, cy / length, cz / length };
        }

        // The point of the rectangle, as an offset from the reference point, for the random numbers u (Ureña et al. 2013, section 4)
        internal static void SampleDirection(SphericalRectangle sr, Vector3f edgeX, Vector3f edgeY, Point2f u, out double wx, out double wy, out double wz)
        {
            // Ureña et al. 2013, section 4: the u and v of the spherical rectangle
            double au = u[0] * sr.solidAngle + sr.k;
            double sinAu = Math.Sin(au), cosAu = Math.Cos(au);
            double fu = (cosAu * sr.b0 - sr.b1) / sinAu;
            double cu = 1 / Math.Sqrt(fu * fu + sr.b0Squared);
            cu = fu >= 0 ? cu : -cu;
            cu = Math.Max(-1, Math.Min(1, cu));
            double xu = -(cu * sr.z0) / Math.Sqrt(Math.Max(1 - cu * cu, 1e-300));
            xu = Math.Max(sr.x0, Math.Min(sr.x1, xu));
            double d = Math.Sqrt(xu * xu + sr.z0 * sr.z0);
            double h0 = sr.y0 / Math.Sqrt(d * d + sr.y0 * sr.y0);
            double h1 = sr.y1 / Math.Sqrt(d * d + sr.y1 * sr.y1);
            double hv = h0 + u[1] * (h1 - h0);
            double hv2 = hv * hv;
            double yv = hv2 < 1 - 1e-12 ? (hv * d) / Math.Sqrt(1 - hv2) : sr.y1;
            yv = Math.Max(sr.y0, Math.Min(sr.y1, yv));

            // the point, in the frame of the reference point: x along the first edge, y along the second, z0 along the (flipped) normal
            wx = xu * edgeX.x + yv * edgeY.x + sr.z0 * sr.zx;
            wy = xu * edgeX.y + yv * edgeY.y + sr.z0 * sr.zy;
            wz = xu * edgeX.z + yv * edgeY.z + sr.z0 * sr.zz;
        }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            if (projectedSampling)
            {
                return polygon.Sample(ctx, u);
            }
            Point3f p = ctx.p();
            SphericalRectangle sr;
            if (!Prepare(corner, edgeX, edgeY, lengthX, lengthY, p, out sr))
            {
                return SampleByArea(ctx, u);
            }

            double wx, wy, wz;
            SampleDirection(sr, edgeX, edgeY, u, out wx, out wy, out wz);
            return polygon.SampleAtPoint((double)p.x + wx, (double)p.y + wy, (double)p.z + wz, ctx.time, (Float)(1 / sr.solidAngle));
        }

        // Area sampling with the density converted to solid angle
        private pstd.optional<ShapeSample> SampleByArea(ShapeSampleContext ctx, Point2f u)
        {
            pstd.optional<ShapeSample> ss = polygon.Sample(u);
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

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            if (projectedSampling)
            {
                return polygon.PDF(ctx, wi);
            }
            Ray ray = ctx.SpawnRay(wi);
            pstd.optional<ShapeIntersection> isect = polygon.Intersect(ray);
            if (!isect.has_value())
            {
                return 0;
            }
            SphericalRectangle sr;
            if (Prepare(corner, edgeX, edgeY, lengthX, lengthY, ctx.p(), out sr))
            {
                return (Float)(1 / sr.solidAngle);
            }
            Float pdf = (1 / Area()) / (pbrt.AbsDot(isect.value().intr.n, -wi) / pbrt.DistanceSquared(ctx.p(), isect.value().intr.p()));
            return pbrt.IsInf(pdf) ? 0 : pdf;
        }

        // Shape "rectanglemesh": "point3 P" (four per rectangle, in order around it), "integer indices" (four per rectangle; if absent the points in order),
        // "point2 uv", "normal N", "integer faceIndices" (one per rectangle), "string sampling" ("solidangle" or "projected")
        public static pstd.vector<Shape> Create(Transform renderFromObject, bool reverseOrientation, ParameterDictionary parameters, FileLoc loc, Allocator alloc,
                                                bool triangleForm = false)
        {
            List<int> indices = parameters.GetIntArray("indices");
            List<Point3f> P = parameters.GetPoint3fArray("P");
            List<Point2f> uv = parameters.GetPoint2fArray("uv");
            List<Normal3f> N = parameters.GetNormal3fArray("N");
            List<int> faceIndices = parameters.GetIntArray("faceIndices");
            if (P.Count == 0)
            {
                pbrt.ErrorExit(loc, "Vertex positions \"P\" must be provided with rectanglemesh shape.");
            }
            if (indices.Count == 0)
            {
                for (int i = 0; i < P.Count; ++i)
                {
                    indices.Add(i);
                }
            }
            if (indices.Count % 4 != 0)
            {
                pbrt.ErrorExit(loc, "rectanglemesh needs four \"indices\" (or four points \"P\") per rectangle.");
            }
            for (int i = 0; i < indices.Count; ++i)
            {
                if (indices[i] < 0 || indices[i] >= P.Count)
                {
                    pbrt.ErrorExit(loc, pbrt.StringPrintf("rectanglemesh has out of-bounds vertex index %d (%d \"P\" values were given).", indices[i], P.Count));
                }
            }
            if (uv.Count != 0 && uv.Count != P.Count)
            {
                pbrt.Error(loc, "Number of \"uv\"s for rectangle mesh must match \"P\"s. Discarding uvs.");
                uv = new List<Point2f>();
            }
            if (N.Count != 0 && N.Count != P.Count)
            {
                pbrt.Error(loc, "Number of \"N\"s for rectangle mesh must match \"P\"s. Discarding \"N\"s.");
                N = new List<Normal3f>();
            }
            int rectangleCount = indices.Count / 4;
            if (faceIndices.Count != 0 && faceIndices.Count != rectangleCount)
            {
                pbrt.Error(loc, "Number of face indices does not match number of rectangles. Discarding face indices.");
                faceIndices = new List<int>();
            }
            string sampling = parameters.GetOneString("sampling", "solidangle");
            bool projected = Ngon.ParseSampling(sampling, loc);

            // (comparison) "rectanglemesh2" is the same shape written in the form of Triangle, see shapes_rectangle2.cs
            if (triangleForm)
            {
                return Rectangle2.CreateShapes(renderFromObject, reverseOrientation, indices, P, N, uv, faceIndices, projected, alloc);
            }
            pstd.vector<Shape> shapes = new pstd.vector<Shape>();
            for (int r = 0; r < rectangleCount; ++r)
            {
                List<int> corner = new List<int> { indices[4 * r], indices[4 * r + 1], indices[4 * r + 2], indices[4 * r + 3] };
                Point3f[] render = new Point3f[4];
                for (int k = 0; k < 4; ++k)
                {
                    render[k] = renderFromObject.Apply(P[corner[k]]);
                }
                pstd.vector<Shape> made = Ngon.CreateShapes(renderFromObject, reverseOrientation, new List<int> { 4 }, corner, P, N, uv,
                                                            faceIndices.Count != 0 ? new List<int> { faceIndices[r] } : new List<int>(), projected, alloc);
                if (made.size() == 1 && made[0] is Ngon && IsRectangle(render))
                {
                    shapes.push_back(new Rectangle(made[0] as Ngon, projected));
                }
                else
                {
                    // not a rectangle (or degenerate): the Ngon (or its triangles), sampled the way an Ngon is
                    for (int i = 0; i < (int)made.size(); ++i)
                    {
                        shapes.push_back(made[i]);
                    }
                }
            }
            return shapes;
        }
    }
}  // namespace pbrt
