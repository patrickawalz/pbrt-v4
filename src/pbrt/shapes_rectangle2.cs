// (not in pbrt-v4) shapes_rectangle2.cs
//
// Shape "rectanglemesh2": the rectangle of shapes_rectangle.cs, written in the form of Triangle (shapes.cs) so that the two can be compared. It does the same
// as Rectangle (it intersects and is shaded as the fan of its four corners, is sampled by solid angle with the area preserving parametrization of the
// spherical rectangle of Ureña, Fajardo and King, and by projected solid angle as an Ngon), with a RectangleMesh of its own and a Rectangle2 that is only
// (meshIndex, rectangleIndex). A rectangle does not hold an Ngon: what the two share is functions, those of PolygonSampling (shapes_polygon_common.cs: the fan
// intersection, the fan geometry and the sampling from a point), and the spherical rectangle of Rectangle (Prepare and SampleDirection).
//
// The corners are given in order around the rectangle: c0, c1 = c0 + a, c2 = c0 + a + b, c3 = c0 + b.

using System;
using System.Collections.Generic;

namespace pbrt
{
    // RectangleMesh Definition: the fans (two triangles) of the rectangles, and for each rectangle where its fan starts, its area, the cumulative areas of the fan
    // and the normal of its plane
    public unsafe class RectangleMesh
    {
        // RectangleMesh Public Methods
        public RectangleMesh(TriangleMesh fan, List<int[]> polygons, bool projectedSampling)
        {
            this.fan = fan;
            this.projectedSampling = projectedSampling;
            nRectangles = polygons.Count;
            firstVertex = new int[nRectangles];
            firstTriangle = new int[nRectangles];
            area = new Float[nRectangles];
            fanAreaCdf = new Float[nRectangles][];
            planeNormal = new Vector3f[nRectangles];
            for (int i = 0; i < nRectangles; ++i)
            {
                firstVertex[i] = polygons[i][0];
                firstTriangle[i] = polygons[i][1];
                PolygonSampling.FanGeometry(fan.p, firstVertex[i], 4, out area[i], out fanAreaCdf[i], out planeNormal[i]);
            }
        }

        public Point3f Corner(int rectangleIndex, int k)
        {
            return fan.p[firstVertex[rectangleIndex] + k];
        }

        // RectangleMesh Public Members
        public TriangleMesh fan;
        public bool projectedSampling;
        public int nRectangles;
        public int[] firstVertex, firstTriangle;
        public Float[] area;
        public Float[][] fanAreaCdf;
        public Vector3f[] planeNormal;
    }

    public sealed unsafe class Rectangle2 : Shape
    {
        // Rectangle2 Public Methods
        public Rectangle2() { }
        public Rectangle2(int meshIndex, int rectangleIndex)
        {
            this.meshIndex = meshIndex;
            this.rectangleIndex = rectangleIndex;
        }

        static List<RectangleMesh> allMeshes;
        static readonly object allMeshesLock = new object();

        public static void Init(Allocator alloc)
        {
            allMeshes = new List<RectangleMesh>();
        }

        RectangleMesh GetMesh()
        {
            return allMeshes[meshIndex];
        }

        public override Float Area()
        {
            return GetMesh().area[rectangleIndex];
        }

        public static pstd.optional<NgonIntersection> IntersectRectangle(in Ray ray, Float tMax, Point3f[] p, int firstVertex)
        {
            if (PolygonSampling.IntersectFan4(ray, tMax, p[firstVertex], p[firstVertex + 1], p[firstVertex + 2], p[firstVertex + 3], out int fan, out double b0,
                                              out double b1, out double b2, out Float t))
            {
                return new NgonIntersection(fan, b0, b1, b2, t);
            }
            return default;
        }

        public static SurfaceInteraction InteractionFromIntersection(RectangleMesh mesh, int rectangleIndex, NgonIntersection ni, Float time, Vector3f wo)
        {
            TriangleIntersection ti = new TriangleIntersection((Float)ni.b0, (Float)ni.b1, (Float)ni.b2, ni.t);
            return Triangle.InteractionFromIntersection(mesh.fan, mesh.firstTriangle[rectangleIndex] + ni.fan, ti, time, wo);
        }

        public override pstd.optional<ShapeIntersection> Intersect(Ray ray, Float tMax = Infinity)
        {
            RectangleMesh mesh = GetMesh();
            pstd.optional<NgonIntersection> ni = IntersectRectangle(ray, tMax, mesh.fan.p, mesh.firstVertex[rectangleIndex]);
            if (!ni)
            {
                return default;
            }
            SurfaceInteraction intr = InteractionFromIntersection(mesh, rectangleIndex, ni.value(), ray.time, -ray.d);
            return new ShapeIntersection(intr, ni.value().t);
        }

        public override bool IntersectP(Ray ray, Float tMax = Infinity)
        {
            RectangleMesh mesh = GetMesh();
            pstd.optional<NgonIntersection> ni = IntersectRectangle(ray, tMax, mesh.fan.p, mesh.firstVertex[rectangleIndex]);
            return ni;
        }

        public override Bounds3f Bounds()
        {
            RectangleMesh mesh = GetMesh();
            return PolygonSampling.FanBounds(mesh.fan.p, mesh.firstVertex[rectangleIndex], 4);
        }

        public override DirectionCone NormalBounds()
        {
            RectangleMesh mesh = GetMesh();
            return PolygonSampling.FanNormalBounds(mesh.fan, mesh.firstVertex[rectangleIndex], 4, mesh.planeNormal[rectangleIndex]);
        }

        public override string ToString()
        {
            RectangleMesh mesh = GetMesh();
            return pbrt.StringPrintf("[ Rectangle2 %f x %f ]", pbrt.Length(mesh.Corner(rectangleIndex, 1) - mesh.Corner(rectangleIndex, 0)),
                                     pbrt.Length(mesh.Corner(rectangleIndex, 3) - mesh.Corner(rectangleIndex, 0)));
        }

        public override Float PDF(Interaction intr)
        {
            return 1 / Area();
        }

        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            RectangleMesh mesh = GetMesh();
            return PolygonSampling.SampleFan(mesh.fan, mesh.firstVertex[rectangleIndex], 4, mesh.fanAreaCdf[rectangleIndex], mesh.area[rectangleIndex],
                                             mesh.planeNormal[rectangleIndex], u);
        }

        // The corner, the unit edges and their lengths of the rectangle
        void Frame(RectangleMesh mesh, out Point3f corner, out Vector3f edgeX, out Vector3f edgeY, out double lengthX, out double lengthY)
        {
            corner = mesh.Corner(rectangleIndex, 0);
            Vector3f ex = mesh.Corner(rectangleIndex, 1) - corner;
            Vector3f ey = mesh.Corner(rectangleIndex, 3) - corner;
            lengthX = pbrt.Length(ex);
            lengthY = pbrt.Length(ey);
            edgeX = ex / (Float)lengthX;
            edgeY = ey / (Float)lengthY;
        }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            RectangleMesh mesh = GetMesh();
            int first = mesh.firstVertex[rectangleIndex];
            if (mesh.projectedSampling)
            {
                return PolygonSampling.SampleFromPoint(mesh.fan, first, 4, mesh.fanAreaCdf[rectangleIndex], mesh.area[rectangleIndex],
                                                       mesh.planeNormal[rectangleIndex], null, true, ctx, u);
            }
            Frame(mesh, out Point3f corner, out Vector3f edgeX, out Vector3f edgeY, out double lengthX, out double lengthY);
            Point3f p = ctx.p();
            if (!Rectangle.Prepare(corner, edgeX, edgeY, lengthX, lengthY, p, out Rectangle.SphericalRectangle sr))
            {
                return PolygonSampling.AreaSampleToSolidAngle(ctx, Sample(u));
            }
            Rectangle.SampleDirection(sr, edgeX, edgeY, u, out double wx, out double wy, out double wz);
            PolygonSampling.FanWeights(mesh.fan.p, first, 4, (double)p.x + wx, (double)p.y + wy, (double)p.z + wz, out int fan, out double b0, out double b1,
                                       out double b2);
            return PolygonSampling.SampleAtFan(mesh.fan, first, mesh.planeNormal[rectangleIndex], fan, b0, b1, b2, ctx.time, true, (Float)(1 / sr.solidAngle));
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            RectangleMesh mesh = GetMesh();
            int first = mesh.firstVertex[rectangleIndex];
            if (mesh.projectedSampling)
            {
                return PolygonSampling.PdfFromPoint(mesh.fan, first, 4, mesh.area[rectangleIndex], mesh.planeNormal[rectangleIndex], null, true, ctx, wi);
            }
            Ray ray = ctx.SpawnRay(wi);
            pstd.optional<NgonIntersection> hit = IntersectRectangle(ray, Infinity, mesh.fan.p, first);
            if (!hit)
            {
                return 0;
            }
            Frame(mesh, out Point3f corner, out Vector3f edgeX, out Vector3f edgeY, out double lengthX, out double lengthY);
            if (Rectangle.Prepare(corner, edgeX, edgeY, lengthX, lengthY, ctx.p(), out Rectangle.SphericalRectangle sr))
            {
                return (Float)(1 / sr.solidAngle);
            }
            NgonIntersection ni = hit.value();
            Point3f pHit = (Float)ni.b0 * mesh.fan.p[first] + (Float)ni.b1 * mesh.fan.p[first + ni.fan + 1] + (Float)ni.b2 * mesh.fan.p[first + ni.fan + 2];
            return PolygonSampling.AreaPdfToSolidAngle(ctx, Area(), (Normal3f)mesh.planeNormal[rectangleIndex], wi, pHit);
        }

        // The Rectangle2 shapes of a mesh of rectangles (the shapes of Ngon2 for the quadrilaterals that are not rectangles, and Triangle shapes for those that
        // are degenerate). indices holds four per rectangle, in order around it. P is in object space. faceIndices has one entry per rectangle (or none).
        public static pstd.vector<Shape> CreateShapes(Transform renderFromObject, bool reverseOrientation, List<int> indices, List<Point3f> P, List<Normal3f> N,
                                                      List<Point2f> uv, List<int> faceIndices, bool projectedSampling, Allocator alloc)
        {
            int rectangleCount = indices.Count / 4;
            List<int> rectangleIndices = new List<int>();
            List<int> rectangleFaces = new List<int>();
            List<int> otherIndices = new List<int>();
            List<int> otherFaces = new List<int>();
            for (int r = 0; r < rectangleCount; ++r)
            {
                Point3f[] render = new Point3f[4];
                for (int k = 0; k < 4; ++k)
                {
                    render[k] = renderFromObject.Apply(P[indices[4 * r + k]]);
                }
                bool rectangle = Rectangle.IsRectangle(render);
                for (int k = 0; k < 4; ++k)
                {
                    (rectangle ? rectangleIndices : otherIndices).Add(indices[4 * r + k]);
                }
                if (faceIndices.Count != 0)
                {
                    (rectangle ? rectangleFaces : otherFaces).Add(faceIndices[r]);
                }
            }

            pstd.vector<Shape> shapes = new pstd.vector<Shape>();
            if (otherIndices.Count != 0)
            {
                pstd.vector<Shape> others = Ngon2.CreateShapes(renderFromObject, reverseOrientation, Fours(otherIndices.Count / 4), otherIndices, P, N, uv, otherFaces,
                                                               projectedSampling, alloc);
                for (int i = 0; i < (int)others.size(); ++i)
                {
                    shapes.push_back(others[i]);
                }
            }
            if (rectangleIndices.Count != 0)
            {
                Ngon.CreateFans(renderFromObject, reverseOrientation, Fours(rectangleIndices.Count / 4), rectangleIndices, P, N, uv, rectangleFaces, alloc, shapes,
                                out TriangleMesh fanMesh, out List<int[]> polygons, out List<int[]> polygonSampleCorners);
                if (polygons.Count != 0)
                {
                    RectangleMesh mesh = new RectangleMesh(fanMesh, polygons, projectedSampling);
                    int meshIndex;
                    lock (allMeshesLock)
                    {
                        meshIndex = allMeshes.Count;
                        allMeshes.Add(mesh);
                    }
                    for (int i = 0; i < polygons.Count; ++i)
                    {
                        shapes.push_back(new Rectangle2(meshIndex, i));
                    }
                }
            }
            return shapes;
        }

        static List<int> Fours(int count)
        {
            List<int> counts = new List<int>();
            for (int i = 0; i < count; ++i)
            {
                counts.Add(4);
            }
            return counts;
        }

        int meshIndex, rectangleIndex;
    }
}  // namespace pbrt
