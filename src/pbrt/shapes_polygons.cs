// (not in pbrt-v4) shapes_polygons.cs
//
// The polygon primitives of the RenderMan Interface: RiPolygon, RiGeneralPolygon (concave, with holes), RiPointsPolygons and
// RiPointsGeneralPolygons. A polygon is triangulated once (the triangulation is exact in the sense that the triangles tile the polygon, there is no
// approximation of the surface) and the triangles are the Triangle shapes of pbrt-v4: their ray intersection is the watertight ray-triangle test, they
// are sampled by area or by spherical triangle sampling, and the per-vertex normals N and texture coordinates (uv or st) are interpolated as for a
// triangle mesh. The triangulation projects the polygon along its Newell normal, joins each hole to the outer loop by a bridge (David Eberly's
// "Triangulation by Ear Clipping") and clips ears; the winding of the outer loop is kept in the triangles, so that the normals follow RenderMan's
// orientation of the polygon and ReverseOrientation applies as for any shape.

using System;
using System.Collections.Generic;

namespace pbrt
{
    public static unsafe class PolygonTriangulator
    {
        // Triangulates the polygon whose first loop is the outer boundary and whose other loops are holes. loops hold indices into points. The result
        // holds three indices per triangle, wound as the outer loop is.
        public static List<int> Triangulate(IList<Point3f> points, IList<int[]> loops)
        {
            List<int> triangles = new List<int>();
            if (loops.Count == 0 || loops[0].Length < 3)
            {
                return triangles;
            }

            // The Newell normal of the outer loop picks the projection
            int[] outer = loops[0];
            double nx = 0, ny = 0, nz = 0;
            for (int i = 0; i < outer.Length; ++i)
            {
                Point3f a = points[outer[i]], b = points[outer[(i + 1) % outer.Length]];
                nx += ((double)a.y - b.y) * ((double)a.z + b.z);
                ny += ((double)a.z - b.z) * ((double)a.x + b.x);
                nz += ((double)a.x - b.x) * ((double)a.y + b.y);
            }
            int axis = Math.Abs(nx) > Math.Abs(ny) && Math.Abs(nx) > Math.Abs(nz) ? 0 : (Math.Abs(ny) > Math.Abs(nz) ? 1 : 2);
            double dominant = axis == 0 ? nx : (axis == 1 ? ny : nz);
            if (dominant == 0)
            {
                return triangles;
            }
            double sign = dominant > 0 ? 1 : -1;
            // the 2D coordinates of every point, such that the outer loop is counterclockwise
            double[] u = new double[points.Count], v = new double[points.Count];
            for (int i = 0; i < points.Count; ++i)
            {
                Point3f p = points[i];
                double a = axis == 0 ? p.y : (axis == 1 ? p.z : p.x);
                double b = axis == 0 ? p.z : (axis == 1 ? p.x : p.y);
                u[i] = sign * a;
                v[i] = b;
            }

            // The holes, clockwise, by decreasing largest u
            List<int[]> holes = new List<int[]>();
            for (int l = 1; l < loops.Count; ++l)
            {
                if (loops[l].Length < 3)
                {
                    continue;
                }
                int[] hole = (int[])loops[l].Clone();
                if (SignedArea(hole, u, v) > 0)
                {
                    Array.Reverse(hole);
                }
                holes.Add(hole);
            }
            holes.Sort((int[] a, int[] b) => MaxU(b, u).CompareTo(MaxU(a, u)));

            List<int> ring = new List<int>(outer);
            if (SignedArea(outer, u, v) < 0)
            {
                ring.Reverse();
            }
            foreach (int[] hole in holes)
            {
                Bridge(ring, hole, u, v);
            }
            bool flipped = SignedArea(outer, u, v) < 0;
            ClipEars(ring, u, v, triangles);
            if (flipped)
            {
                // the loop was reversed to work counterclockwise: restore its winding
                for (int t = 0; t < triangles.Count; t += 3)
                {
                    int tmp = triangles[t + 1];
                    triangles[t + 1] = triangles[t + 2];
                    triangles[t + 2] = tmp;
                }
            }
            return triangles;
        }

        private static double SignedArea(int[] loop, double[] u, double[] v)
        {
            double area = 0;
            for (int i = 0; i < loop.Length; ++i)
            {
                int a = loop[i], b = loop[(i + 1) % loop.Length];
                area += u[a] * v[b] - u[b] * v[a];
            }
            return 0.5 * area;
        }

        private static double MaxU(int[] loop, double[] u)
        {
            double result = double.NegativeInfinity;
            foreach (int i in loop)
            {
                result = Math.Max(result, u[i]);
            }
            return result;
        }

        private static double Cross(double ax, double ay, double bx, double by, double cx, double cy)
        {
            return (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
        }

        // Joins the clockwise hole to the counterclockwise ring by a bridge from the hole's vertex of largest u to a vertex of the ring that it sees
        private static void Bridge(List<int> ring, int[] hole, double[] u, double[] v)
        {
            int m = 0;
            for (int i = 1; i < hole.Length; ++i)
            {
                if (u[hole[i]] > u[hole[m]])
                {
                    m = i;
                }
            }
            double mx = u[hole[m]], my = v[hole[m]];

            // The nearest edge of the ring that the ray from M towards +u crosses
            double bestX = double.PositiveInfinity;
            int edgeStart = -1, candidate = -1;
            for (int i = 0; i < ring.Count; ++i)
            {
                int ia = ring[i], ib = ring[(i + 1) % ring.Count];
                double ay = v[ia], by = v[ib];
                if (ay == by || (my < Math.Min(ay, by)) || (my > Math.Max(ay, by)))
                {
                    continue;
                }
                double x = u[ia] + (my - ay) * (u[ib] - u[ia]) / (by - ay);
                if (x >= mx && x < bestX)
                {
                    bestX = x;
                    edgeStart = i;
                    candidate = u[ia] > u[ib] ? i : (i + 1) % ring.Count;
                }
            }
            if (candidate < 0)
            {
                // The hole is not inside the ring: join it to the closest vertex
                double best = double.PositiveInfinity;
                for (int i = 0; i < ring.Count; ++i)
                {
                    double d = (u[ring[i]] - mx) * (u[ring[i]] - mx) + (v[ring[i]] - my) * (v[ring[i]] - my);
                    if (d < best)
                    {
                        best = d;
                        candidate = i;
                    }
                }
            }
            else
            {
                // A vertex of the ring inside the triangle (M, I, candidate) hides the candidate: take the one that is the closest in angle to the ray
                double px = u[ring[candidate]], py = v[ring[candidate]];
                double ix = bestX, iy = my;
                double bestTan = double.PositiveInfinity;
                int chosen = candidate;
                for (int i = 0; i < ring.Count; ++i)
                {
                    if (i == candidate)
                    {
                        continue;
                    }
                    double x = u[ring[i]], y = v[ring[i]];
                    if (x == px && y == py)
                    {
                        continue;
                    }
                    // inside the triangle M, I, P (either winding)
                    double c0 = Cross(mx, my, ix, iy, x, y), c1 = Cross(ix, iy, px, py, x, y), c2 = Cross(px, py, mx, my, x, y);
                    bool inside = (c0 >= 0 && c1 >= 0 && c2 >= 0) || (c0 <= 0 && c1 <= 0 && c2 <= 0);
                    if (!inside || x < mx)
                    {
                        continue;
                    }
                    double tangent = Math.Abs(y - my) / Math.Max(1e-300, x - mx);
                    if (tangent < bestTan || (tangent == bestTan && x < u[ring[chosen]]))
                    {
                        bestTan = tangent;
                        chosen = i;
                    }
                }
                candidate = chosen;
            }

            // A vertex that earlier bridges visit more than once: join to the visit whose interior angle contains the direction to M
            int vertex = ring[candidate];
            for (int k = 0; k < ring.Count; ++k)
            {
                if (ring[k] != vertex)
                {
                    continue;
                }
                int before = ring[(k + ring.Count - 1) % ring.Count], after = ring[(k + 1) % ring.Count];
                double ax = u[before] - u[vertex], ay = v[before] - v[vertex];
                double bx = u[after] - u[vertex], by = v[after] - v[vertex];
                double dx = mx - u[vertex], dy = my - v[vertex];
                // the interior of a counterclockwise ring is the angle from the direction of the next vertex, counterclockwise, to that of the previous
                bool inside;
                if (bx * ay - by * ax >= 0)
                {
                    inside = (bx * dy - by * dx) >= 0 && (dx * ay - dy * ax) >= 0;
                }
                else
                {
                    inside = !((ax * dy - ay * dx) > 0 && (dx * by - dy * bx) > 0);
                }
                if (inside)
                {
                    candidate = k;
                    break;
                }
            }

            // ... V, M, hole..., M, V ...
            List<int> spliced = new List<int>(hole.Length + 2);
            for (int k = 0; k <= hole.Length; ++k)
            {
                spliced.Add(hole[(m + k) % hole.Length]);
            }
            spliced.Add(ring[candidate]);
            ring.InsertRange(candidate + 1, spliced);
        }

        private static void ClipEars(List<int> ring, double[] u, double[] v, List<int> triangles)
        {
            // Zero-area corners (the spikes where a bridge meets itself, repeated or collinear vertices) bound no area: remove them first
            double scale = 0;
            foreach (int w in ring)
            {
                scale = Math.Max(scale, Math.Max(Math.Abs(u[w]), Math.Abs(v[w])));
            }
            double epsilon = 1e-14 * scale * scale;
            while (ring.Count > 3)
            {
                int n = ring.Count;
                bool degenerate = false;
                for (int i = 0; i < n; ++i)
                {
                    int a = ring[(i + n - 1) % n], b = ring[i], c = ring[(i + 1) % n];
                    if (Math.Abs(Cross(u[a], v[a], u[b], v[b], u[c], v[c])) <= epsilon)
                    {
                        ring.RemoveAt(i);
                        degenerate = true;
                        break;
                    }
                }
                if (degenerate)
                {
                    continue;
                }
                bool found = false;
                double smallest = double.PositiveInfinity;
                int smallestIndex = 0;
                for (int i = 0; i < n; ++i)
                {
                    int a = ring[(i + n - 1) % n], b = ring[i], c = ring[(i + 1) % n];
                    double area = Cross(u[a], v[a], u[b], v[b], u[c], v[c]);
                    if (Math.Abs(area) < smallest)
                    {
                        smallest = Math.Abs(area);
                        smallestIndex = i;
                    }
                    if (!(area > 0))
                    {
                        continue;
                    }
                    if (!ContainsVertex(ring, i, a, b, c, u, v))
                    {
                        triangles.Add(a);
                        triangles.Add(b);
                        triangles.Add(c);
                        ring.RemoveAt(i);
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    // No ear (collinear vertices, or a polygon that crosses itself): drop the vertex of the smallest triangle
                    ring.RemoveAt(smallestIndex);
                }
            }
            if (ring.Count == 3 && Cross(u[ring[0]], v[ring[0]], u[ring[1]], v[ring[1]], u[ring[2]], v[ring[2]]) > 0)
            {
                triangles.Add(ring[0]);
                triangles.Add(ring[1]);
                triangles.Add(ring[2]);
            }
        }

        // Whether another vertex of the ring lies in the triangle (a, b, c) of the ear at position i (vertices at the same place as a corner do not count)
        private static bool ContainsVertex(List<int> ring, int i, int a, int b, int c, double[] u, double[] v)
        {
            double minX = Math.Min(u[a], Math.Min(u[b], u[c])), maxX = Math.Max(u[a], Math.Max(u[b], u[c]));
            double minY = Math.Min(v[a], Math.Min(v[b], v[c])), maxY = Math.Max(v[a], Math.Max(v[b], v[c]));
            int n = ring.Count;
            for (int k = 0; k < n; ++k)
            {
                if (k == i || k == (i + n - 1) % n || k == (i + 1) % n)
                {
                    continue;
                }
                int w = ring[k];
                int before = ring[(k + n - 1) % n], after = ring[(k + 1) % n];
                if (Cross(u[before], v[before], u[w], v[w], u[after], v[after]) > 0)
                {
                    // a convex vertex inside the triangle implies a reflex one does too
                    continue;
                }
                double x = u[w], y = v[w];
                if (x < minX || x > maxX || y < minY || y > maxY)
                {
                    continue;
                }
                if ((x == u[a] && y == v[a]) || (x == u[b] && y == v[b]) || (x == u[c] && y == v[c]))
                {
                    continue;
                }
                if (Cross(u[a], v[a], u[b], v[b], x, y) >= 0 && Cross(u[b], v[b], u[c], v[c], x, y) >= 0 &&
                    Cross(u[c], v[c], u[a], v[a], x, y) >= 0)
                {
                    return true;
                }
            }
            return false;
        }
    }

    // The creation of the polygon primitives: "polygon", "generalpolygon", "pointspolygons" and "pointsgeneralpolygons"
    public static unsafe class Polygons
    {
        // Triangle shapes for polygons given as loops of indices into the vertex arrays; polygonLoops[k] holds the loops of polygon k (outer first)
        public static pstd.vector<Shape> CreateShapes(Transform renderFromObject, bool reverseOrientation, List<Point3f> points,
                                                      List<Normal3f> normals, List<Point2f> uvs, List<List<int[]>> polygonLoops, Allocator alloc)
        {
            List<int> indices = new List<int>();
            List<int> faces = new List<int>();
            for (int k = 0; k < polygonLoops.Count; ++k)
            {
                List<int> triangles = PolygonTriangulator.Triangulate(points, polygonLoops[k]);
                indices.AddRange(triangles);
                for (int t = 0; t < triangles.Count / 3; ++t)
                {
                    faces.Add(k);
                }
            }
            if (indices.Count == 0)
            {
                return new pstd.vector<Shape>();
            }
            TriangleMesh mesh = new TriangleMesh(renderFromObject, reverseOrientation, new pstd.vector<int>(indices), new pstd.vector<Point3f>(points),
                                                 new pstd.vector<Vector3f>(), new pstd.vector<Normal3f>(normals), new pstd.vector<Point2f>(uvs),
                                                 new pstd.vector<int>(faces), alloc);
            return Triangle.CreateTriangles(mesh, alloc);
        }

        public static pstd.vector<Shape> Create(string name, Transform renderFromObject, bool reverseOrientation, ParameterDictionary parameters,
                                                FileLoc loc, Allocator alloc)
        {
            List<Point3f> points = parameters.GetPoint3fArray("P");
            if (points.Count == 0)
            {
                pbrt.Error(loc, name + ": \"P\" must be provided.");
                return new pstd.vector<Shape>();
            }
            List<Normal3f> normals = parameters.GetNormal3fArray("N");
            // RenderMan calls the texture coordinates st
            List<Point2f> uvs = parameters.GetPoint2fArray("uv");
            if (uvs.Count == 0)
            {
                uvs = parameters.GetPoint2fArray("st");
            }
            if (normals.Count != 0 && normals.Count != points.Count)
            {
                pbrt.Error(loc, name + ": the number of \"N\" must match \"P\". Discarding them.");
                normals = new List<Normal3f>();
            }
            if (uvs.Count != 0 && uvs.Count != points.Count)
            {
                pbrt.Error(loc, name + ": the number of \"uv\" must match \"P\". Discarding them.");
                uvs = new List<Point2f>();
            }

            List<List<int[]>> polygons = new List<List<int[]>>();
            if (name == "polygon")
            {
                int[] loop = new int[points.Count];
                for (int i = 0; i < loop.Length; ++i)
                {
                    loop[i] = i;
                }
                polygons.Add(new List<int[]> { loop });
            }
            else if (name == "generalpolygon")
            {
                // nvertices: the size of each loop (the first is the outer boundary); P: the vertices of all the loops
                List<int> sizes = parameters.GetIntArray("nvertices");
                List<int[]> loops = new List<int[]>();
                int start = 0;
                foreach (int size in sizes)
                {
                    if (start + size > points.Count)
                    {
                        pbrt.Error(loc, name + ": \"nvertices\" runs past \"P\".");
                        return new pstd.vector<Shape>();
                    }
                    int[] loop = new int[size];
                    for (int i = 0; i < size; ++i)
                    {
                        loop[i] = start + i;
                    }
                    loops.Add(loop);
                    start += size;
                }
                polygons.Add(loops);
            }
            else
            {
                List<int> sizes = parameters.GetIntArray("nvertices");
                List<int> vertices = parameters.GetIntArray("vertices");
                for (int i = 0; i < vertices.Count; ++i)
                {
                    if (vertices[i] < 0 || vertices[i] >= points.Count)
                    {
                        pbrt.Error(loc, name + ": out of range vertex index.");
                        return new pstd.vector<Shape>();
                    }
                }
                int cursor = 0;
                if (name == "pointspolygons")
                {
                    foreach (int size in sizes)
                    {
                        if (cursor + size > vertices.Count)
                        {
                            pbrt.Error(loc, name + ": \"nvertices\" runs past \"vertices\".");
                            return new pstd.vector<Shape>();
                        }
                        int[] loop = new int[size];
                        for (int i = 0; i < size; ++i)
                        {
                            loop[i] = vertices[cursor + i];
                        }
                        cursor += size;
                        polygons.Add(new List<int[]> { loop });
                    }
                }
                else
                {
                    // pointsgeneralpolygons: nloops loops per polygon, nvertices per loop
                    List<int> loopCounts = parameters.GetIntArray("nloops");
                    int loopIndex = 0;
                    foreach (int count in loopCounts)
                    {
                        List<int[]> loops = new List<int[]>();
                        for (int l = 0; l < count; ++l)
                        {
                            if (loopIndex >= sizes.Count || cursor + sizes[loopIndex] > vertices.Count)
                            {
                                pbrt.Error(loc, name + ": \"nvertices\" or \"vertices\" is too short.");
                                return new pstd.vector<Shape>();
                            }
                            int size = sizes[loopIndex++];
                            int[] loop = new int[size];
                            for (int i = 0; i < size; ++i)
                            {
                                loop[i] = vertices[cursor + i];
                            }
                            cursor += size;
                            loops.Add(loop);
                        }
                        polygons.Add(loops);
                    }
                }
            }
            return CreateShapes(renderFromObject, reverseOrientation, points, normals, uvs, polygons, alloc);
        }
    }
}  // namespace pbrt
