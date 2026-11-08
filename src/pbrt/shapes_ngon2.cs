// (not in pbrt-v4) shapes_ngon2.cs
//
// Shape "ngonmesh2": the planar convex polygon of shapes_ngon.cs, written in the form of Triangle (shapes.cs) so that the two can be compared. It does the same
// as Ngon (the same watertight fan intersection, the same interaction of the triangle of the fan, the same sampling by area, by solid angle and by projected
// solid angle, the same classification), with
//
//  * an NgonMesh that holds what Ngon holds in every shape (the fan triangles, and for each polygon where it starts, its corner count, area, the cumulative
//    areas of its fan, the normal of its plane and the corners that the projected sampling uses),
//  * a Ngon2 that is only (meshIndex, polygonIndex), as Triangle is,
//  * a free function IntersectNgon(ray, tMax, p, firstVertex, cornerCount) that returns an optional NgonIntersection, as IntersectTriangle does,
//  * a static InteractionFromIntersection(mesh, polygonIndex, ni, time, wo) that is the interaction of the triangle of the fan,
//  * Area, Bounds, NormalBounds, Sample, PDF in the order of Triangle,
//  * the parts that other polygon shapes need as well (the fan intersection, the fan geometry, the sampling by projected solid angle, the conversion of the
//    density of a sample by area) as functions of PolygonSampling (shapes_polygon_common.cs).
//
// What is shared with Ngon: Classify, EarClip, PlanarCoordinates and the building of the fans (CreateFans).

using System;
using System.Collections.Generic;

namespace pbrt
{
    // NgonMesh Definition
    public unsafe class NgonMesh
    {
        // NgonMesh Public Methods
        public NgonMesh(TriangleMesh fan, List<int[]> polygons, List<int[]> polygonSampleCorners, bool projectedSampling)
        {
            this.fan = fan;
            this.projectedSampling = projectedSampling;
            nPolygons = polygons.Count;
            firstVertex = new int[nPolygons];
            firstTriangle = new int[nPolygons];
            cornerCount = new int[nPolygons];
            sampleCorners = polygonSampleCorners.ToArray();
            area = new Float[nPolygons];
            fanAreaCdf = new Float[nPolygons][];
            planeNormal = new Vector3f[nPolygons];
            for (int i = 0; i < nPolygons; ++i)
            {
                firstVertex[i] = polygons[i][0];
                firstTriangle[i] = polygons[i][1];
                cornerCount[i] = polygons[i][2];
                PolygonSampling.FanGeometry(fan.p, firstVertex[i], cornerCount[i], out area[i], out fanAreaCdf[i], out planeNormal[i]);
            }
        }

        public Point3f Corner(int polygonIndex, int k)
        {
            return fan.p[firstVertex[polygonIndex] + k];
        }

        // NgonMesh Public Members
        public TriangleMesh fan;
        public bool projectedSampling;
        public int nPolygons;
        public int[] firstVertex, firstTriangle, cornerCount;
        public int[][] sampleCorners;
        public Float[] area;
        public Float[][] fanAreaCdf;
        public Vector3f[] planeNormal;
    }

    // NgonIntersection Definition: the weights are those of the corners (v0, v[fan + 1], v[fan + 2]) of the triangle of the fan that was hit
    public struct NgonIntersection
    {
        public NgonIntersection(int fan, double b0, double b1, double b2, Float t)
        {
            this.fan = fan;
            this.b0 = b0;
            this.b1 = b1;
            this.b2 = b2;
            this.t = t;
        }

        public int fan;
        public double b0, b1, b2;
        public Float t;
    }

    public sealed unsafe class Ngon2 : Shape
    {
        // Ngon2 Public Methods
        public Ngon2() { }
        public Ngon2(int meshIndex, int polygonIndex)
        {
            this.meshIndex = meshIndex;
            this.polygonIndex = polygonIndex;
        }

        static List<NgonMesh> allMeshes;
        static readonly object allMeshesLock = new object();

        public static void Init(Allocator alloc)
        {
            allMeshes = new List<NgonMesh>();
        }

        NgonMesh GetMesh()
        {
            return allMeshes[meshIndex];
        }

        public override Float Area()
        {
            return GetMesh().area[polygonIndex];
        }

        public static pstd.optional<NgonIntersection> IntersectNgon(in Ray ray, Float tMax, Point3f[] p, int firstVertex, int cornerCount)
        {
            if (PolygonSampling.IntersectFan(ray, tMax, p, null, firstVertex, cornerCount, 0, out int fan, out double b0, out double b1, out double b2, out Float t))
            {
                return new NgonIntersection(fan, b0, b1, b2, t);
            }
            return default;
        }

        public static SurfaceInteraction InteractionFromIntersection(NgonMesh mesh, int polygonIndex, NgonIntersection ni, Float time, Vector3f wo)
        {
            TriangleIntersection ti = new TriangleIntersection((Float)ni.b0, (Float)ni.b1, (Float)ni.b2, ni.t);
            return Triangle.InteractionFromIntersection(mesh.fan, mesh.firstTriangle[polygonIndex] + ni.fan, ti, time, wo);
        }

        public override pstd.optional<ShapeIntersection> Intersect(Ray ray, Float tMax = Infinity)
        {
            NgonMesh mesh = GetMesh();
            pstd.optional<NgonIntersection> ni = IntersectNgon(ray, tMax, mesh.fan.p, mesh.firstVertex[polygonIndex], mesh.cornerCount[polygonIndex]);
            if (!ni)
            {
                return default;
            }
            SurfaceInteraction intr = InteractionFromIntersection(mesh, polygonIndex, ni.value(), ray.time, -ray.d);
            return new ShapeIntersection(intr, ni.value().t);
        }

        public override bool IntersectP(Ray ray, Float tMax = Infinity)
        {
            NgonMesh mesh = GetMesh();
            pstd.optional<NgonIntersection> ni = IntersectNgon(ray, tMax, mesh.fan.p, mesh.firstVertex[polygonIndex], mesh.cornerCount[polygonIndex]);
            return ni;
        }

        public override Bounds3f Bounds()
        {
            NgonMesh mesh = GetMesh();
            return PolygonSampling.FanBounds(mesh.fan.p, mesh.firstVertex[polygonIndex], mesh.cornerCount[polygonIndex]);
        }

        public override DirectionCone NormalBounds()
        {
            NgonMesh mesh = GetMesh();
            return PolygonSampling.FanNormalBounds(mesh.fan, mesh.firstVertex[polygonIndex], mesh.cornerCount[polygonIndex], mesh.planeNormal[polygonIndex]);
        }

        public override string ToString()
        {
            NgonMesh mesh = GetMesh();
            return pbrt.StringPrintf("[ Ngon2 corners: %d firstVertex: %d area: %f ]", mesh.cornerCount[polygonIndex], mesh.firstVertex[polygonIndex], Area());
        }

        public override Float PDF(Interaction intr)
        {
            return 1 / Area();
        }

        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            NgonMesh mesh = GetMesh();
            return PolygonSampling.SampleFan(mesh.fan, mesh.firstVertex[polygonIndex], mesh.cornerCount[polygonIndex], mesh.fanAreaCdf[polygonIndex],
                                             mesh.area[polygonIndex], mesh.planeNormal[polygonIndex], u);
        }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            NgonMesh mesh = GetMesh();
            return PolygonSampling.SampleFromPoint(mesh.fan, mesh.firstVertex[polygonIndex], mesh.cornerCount[polygonIndex], mesh.fanAreaCdf[polygonIndex],
                                                   mesh.area[polygonIndex], mesh.planeNormal[polygonIndex], mesh.sampleCorners[polygonIndex],
                                                   mesh.projectedSampling, ctx, u);
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            NgonMesh mesh = GetMesh();
            return PolygonSampling.PdfFromPoint(mesh.fan, mesh.firstVertex[polygonIndex], mesh.cornerCount[polygonIndex], mesh.area[polygonIndex],
                                                mesh.planeNormal[polygonIndex], mesh.sampleCorners[polygonIndex], mesh.projectedSampling, ctx, wi);
        }

        // The Ngon2 shapes (and the Triangle shapes of the polygons that are not planar and convex) of a mesh of polygons. counts holds the corner count of each
        // polygon and indices the corners of all of them, in order around each. P is in object space. faceIndices has one entry per polygon (or none).
        public static pstd.vector<Shape> CreateShapes(Transform renderFromObject, bool reverseOrientation, List<int> counts, List<int> indices, List<Point3f> P,
                                                      List<Normal3f> N, List<Point2f> uv, List<int> faceIndices, bool projectedSampling, Allocator alloc)
        {
            pstd.vector<Shape> shapes = new pstd.vector<Shape>();
            Ngon.CreateFans(renderFromObject, reverseOrientation, counts, indices, P, N, uv, faceIndices, alloc, shapes, out TriangleMesh fanMesh,
                            out List<int[]> polygons, out List<int[]> polygonSampleCorners);
            if (polygons.Count != 0)
            {
                NgonMesh mesh = new NgonMesh(fanMesh, polygons, polygonSampleCorners, projectedSampling);
                int meshIndex;
                lock (allMeshesLock)
                {
                    meshIndex = allMeshes.Count;
                    allMeshes.Add(mesh);
                }
                for (int i = 0; i < polygons.Count; ++i)
                {
                    shapes.push_back(new Ngon2(meshIndex, i));
                }
            }
            return shapes;
        }

        int meshIndex, polygonIndex;
    }
}  // namespace pbrt
