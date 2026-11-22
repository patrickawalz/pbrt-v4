// (not in pbrt-v4) sdfcarving.cs
//
// Sphere Carving (Schott, Thonat, Lambert, Guerin, Galin and Paris, "Sphere Carving: Bounding Volumes for Signed Distance
// Fields", 2025): a convex bounding volume for a field that is only known through its values. The value of a conservative field
// at a point is the radius of a ball about the point that the surface does not enter. Starting from points on a large sphere V0
// around the object, the balls of the points carve the space away; the points where three of the balls meet on the border of what
// is left are the next points, and so on until the balls become small. The object stays inside the convex hull of the points of
// every round (the second property of the paper).
//
// The hull is reduced to a few of its faces (a k-means over the directions of the faces, section 5.1: leaving a face out only
// adds volume), and the field is replaced by the distance to these planes outside of them (SdfCarvedField), which is the cheap
// proxy of the paper: f~(p) = d(p, B) if d(p, B) > epsilon, f(p) otherwise. The convex decomposition of section 5.2 is not here.
//
// A field with a Lipschitz bound L above 1 gives the balls of the radius f / L. The field has to be a proven bound of the
// distance (SdfNode.IsRigorous): a fractal estimate that is too large far from the surface would carve into the object.

using System;
using System.Collections.Generic;

namespace pbrt
{

    // SdfCarving Definition
    public sealed class SdfCarving
    {
        // SdfCarving Public Methods

        // Carves from the sphere V0 of the given center and radius, which has to hold the object. threshold is the radius below
        // which a point gets no ball (tau in the paper); the spheres and the rounds are limited.
        public SdfCarving(SdfNode field, SdfVector center, double radius, double threshold, int maxSpheres, int maxIterations)
        {
            this.field = field;
            this.center = center;
            this.radius = radius;
            lipschitz = Math.Max(field.Lipschitz(), 1e-30);
            valid = Initialize(maxSpheres);
            if (!valid)
            {
                return;
            }
            for (iterations = 0; iterations < maxIterations; ++iterations)
            {
                if (!Iterate(threshold, maxSpheres))
                {
                    break;
                }
            }
            valid = pointCount >= 4;
        }

        // Whether the carving started (the balls of the first points have to cover V0) and left points
        public bool IsValid()
        {
            return valid;
        }

        public int Evaluations()
        {
            return evaluations;
        }

        public int Iterations()
        {
            return iterations;
        }

        public int SphereCount()
        {
            return sphereCount;
        }

        public int PointCount()
        {
            return pointCount;
        }

        public SdfVector Point(int index)
        {
            return new SdfVector(pointX[index], pointY[index], pointZ[index]);
        }

        // The volume of the convex hull of the points
        public double Volume()
        {
            return HullVolume(pointX, pointY, pointZ, pointCount, radius);
        }

        // The box of the points
        public void Bounds(out SdfVector lower, out SdfVector upper)
        {
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
            for (int i = 0; i < pointCount; ++i)
            {
                minX = Math.Min(minX, pointX[i]); minY = Math.Min(minY, pointY[i]); minZ = Math.Min(minZ, pointZ[i]);
                maxX = Math.Max(maxX, pointX[i]); maxY = Math.Max(maxY, pointY[i]); maxZ = Math.Max(maxZ, pointZ[i]);
            }
            lower = new SdfVector(minX, minY, minZ);
            upper = new SdfVector(maxX, maxY, maxZ);
        }

        // The planes of a convex volume around the points, four numbers each (the unit normal and the offset: a point p is inside
        // where n . p <= offset for every plane): the six sides of the box of the points, and up to `count` faces of the convex
        // hull of the points chosen by a k-means over the directions of the faces. Every offset is the largest n . p over the
        // points, so the planes hold all of the points whatever the precision of the hull.
        public double[] Planes(int count)
        {
            List<double> planes = new List<double>();
            for (int axis = 0; axis < 3; ++axis)
            {
                AddPlane(planes, axis == 0 ? 1 : 0, axis == 1 ? 1 : 0, axis == 2 ? 1 : 0);
                AddPlane(planes, axis == 0 ? -1 : 0, axis == 1 ? -1 : 0, axis == 2 ? -1 : 0);
            }
            List<double> normals = HullNormals();
            int faces = normals.Count / 3;
            if (faces == 0 || count <= 0)
            {
                return planes.ToArray();
            }
            // The first centers: the face directions that are farthest from the centers so far
            int clusters = Math.Min(count, faces);
            double[] centers = new double[3 * clusters];
            double[] farthest = new double[faces];
            for (int i = 0; i < faces; ++i)
            {
                farthest[i] = double.PositiveInfinity;
            }
            int pick = 0;
            for (int c = 0; c < clusters; ++c)
            {
                centers[3 * c] = normals[3 * pick];
                centers[3 * c + 1] = normals[3 * pick + 1];
                centers[3 * c + 2] = normals[3 * pick + 2];
                double best = -1;
                for (int i = 0; i < faces; ++i)
                {
                    double dot = normals[3 * i] * centers[3 * c] + normals[3 * i + 1] * centers[3 * c + 1] + normals[3 * i + 2] * centers[3 * c + 2];
                    farthest[i] = Math.Min(farthest[i], 1 - dot);
                    if (farthest[i] > best)
                    {
                        best = farthest[i];
                        pick = i;
                    }
                }
            }
            int[] owner = new int[faces];
            for (int round = 0; round < 16; ++round)
            {
                Assign(normals, centers, clusters, owner);
                double[] sums = new double[3 * clusters];
                for (int i = 0; i < faces; ++i)
                {
                    sums[3 * owner[i]] += normals[3 * i];
                    sums[3 * owner[i] + 1] += normals[3 * i + 1];
                    sums[3 * owner[i] + 2] += normals[3 * i + 2];
                }
                for (int c = 0; c < clusters; ++c)
                {
                    double length = Math.Sqrt(sums[3 * c] * sums[3 * c] + sums[3 * c + 1] * sums[3 * c + 1] + sums[3 * c + 2] * sums[3 * c + 2]);
                    if (length > 0)
                    {
                        centers[3 * c] = sums[3 * c] / length;
                        centers[3 * c + 1] = sums[3 * c + 1] / length;
                        centers[3 * c + 2] = sums[3 * c + 2] / length;
                    }
                }
            }
            // Each cluster gives the face of the hull whose direction is closest to its center
            for (int c = 0; c < clusters; ++c)
            {
                int closest = -1;
                double best = -2;
                for (int i = 0; i < faces; ++i)
                {
                    double dot = normals[3 * i] * centers[3 * c] + normals[3 * i + 1] * centers[3 * c + 1] + normals[3 * i + 2] * centers[3 * c + 2];
                    if (dot > best)
                    {
                        best = dot;
                        closest = i;
                    }
                }
                AddPlane(planes, normals[3 * closest], normals[3 * closest + 1], normals[3 * closest + 2]);
            }
            return planes.ToArray();
        }

        // SdfCarving Private Methods
        private static void Assign(List<double> normals, double[] centers, int clusters, int[] owner)
        {
            for (int i = 0; i < owner.Length; ++i)
            {
                double best = -2;
                for (int c = 0; c < clusters; ++c)
                {
                    double dot = normals[3 * i] * centers[3 * c] + normals[3 * i + 1] * centers[3 * c + 1] + normals[3 * i + 2] * centers[3 * c + 2];
                    if (dot > best)
                    {
                        best = dot;
                        owner[i] = c;
                    }
                }
            }
        }

        private void AddPlane(List<double> planes, double nx, double ny, double nz)
        {
            double offset = double.NegativeInfinity;
            for (int i = 0; i < pointCount; ++i)
            {
                offset = Math.Max(offset, nx * pointX[i] + ny * pointY[i] + nz * pointZ[i]);
            }
            planes.Add(nx);
            planes.Add(ny);
            planes.Add(nz);
            planes.Add(offset);
        }

        // The first points: the corners of an icosahedron on V0, divided until the balls of the corners of every triangle cover
        // the triangle (section 4.1: the first balls have to cover all of V0, or the carving could leave a part of the object out)
        private bool Initialize(int maxSpheres)
        {
            double t = 0.5 * (1 + Math.Sqrt(5));
            List<SdfVector> corners = new List<SdfVector> {
                new SdfVector(-1, t, 0), new SdfVector(1, t, 0), new SdfVector(-1, -t, 0), new SdfVector(1, -t, 0),
                new SdfVector(0, -1, t), new SdfVector(0, 1, t), new SdfVector(0, -1, -t), new SdfVector(0, 1, -t),
                new SdfVector(t, 0, -1), new SdfVector(t, 0, 1), new SdfVector(-t, 0, -1), new SdfVector(-t, 0, 1) };
            List<int> triangles = new List<int> {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
            for (int i = 0; i < corners.Count; ++i)
            {
                corners[i] = corners[i].Normalized();
            }
            for (int level = 0; level < 5; ++level)
            {
                if (corners.Count > maxSpheres)
                {
                    return false;
                }
                double[] radii = new double[corners.Count];
                bool positive = true;
                for (int i = 0; i < corners.Count; ++i)
                {
                    radii[i] = field.Evaluate(center + corners[i] * radius) / lipschitz;
                    ++evaluations;
                    positive = positive && radii[i] > 0;
                }
                if (!positive)
                {
                    // The surface reaches V0
                    return false;
                }
                bool covered = true;
                for (int i = 0; i < triangles.Count && covered; i += 3)
                {
                    SdfVector a = corners[triangles[i]], b = corners[triangles[i + 1]], c = corners[triangles[i + 2]];
                    SdfVector middle = (a + b + c).Normalized();
                    covered = (middle - a).Length() * radius < 0.95 * radii[triangles[i]] ||
                              (middle - b).Length() * radius < 0.95 * radii[triangles[i + 1]] ||
                              (middle - c).Length() * radius < 0.95 * radii[triangles[i + 2]];
                }
                if (covered)
                {
                    for (int i = 0; i < corners.Count; ++i)
                    {
                        AddSphere(center + corners[i] * radius, radii[i]);
                    }
                    Intersections(0);
                    return true;
                }
                // Each triangle into four
                Dictionary<long, int> middles = new Dictionary<long, int>();
                List<int> divided = new List<int>();
                for (int i = 0; i < triangles.Count; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    int ab = Middle(corners, middles, a, b), bc = Middle(corners, middles, b, c), ca = Middle(corners, middles, c, a);
                    divided.AddRange(new int[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                triangles = divided;
            }
            return false;
        }

        private static int Middle(List<SdfVector> corners, Dictionary<long, int> middles, int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (!middles.TryGetValue(key, out int index))
            {
                index = corners.Count;
                corners.Add((corners[a] + corners[b]).Normalized());
                middles.Add(key, index);
            }
            return index;
        }

        // One round (section 4.2): balls at the points that are far enough from the surface, then the points where three balls
        // meet on the border of what is left. false when no ball was added. Where the paper gives every point its ball at once,
        // a round here only places the balls of at least half the radius of the largest one that is waiting: a large ball
        // often carves away the points of the small ones next to it, which a field with a Lipschitz bound above 1 (whose balls
        // are small) would otherwise pay for by the thousand. The others wait for a later round with their value kept.
        private bool Iterate(double threshold, int maxSpheres)
        {
            int first = sphereCount;
            double largest = 0;
            for (int i = 0; i < pointCount; ++i)
            {
                if (!pointEvaluated[i])
                {
                    pointEvaluated[i] = true;
                    pointValue[i] = field.Evaluate(new SdfVector(pointX[i], pointY[i], pointZ[i])) / lipschitz;
                    ++evaluations;
                }
                largest = Math.Max(largest, pointValue[i]);
            }
            for (int i = 0; i < pointCount && sphereCount < maxSpheres; ++i)
            {
                if (pointValue[i] > threshold && pointValue[i] >= 0.5 * largest)
                {
                    AddSphere(new SdfVector(pointX[i], pointY[i], pointZ[i]), pointValue[i]);
                    // The point is inside its own ball and goes with the carved ones below
                }
            }
            if (sphereCount == first)
            {
                return false;
            }
            // The points that the new balls carved away
            int kept = 0;
            for (int i = 0; i < pointCount; ++i)
            {
                bool carved = false;
                for (int s = first; s < sphereCount && !carved; ++s)
                {
                    carved = Inside(s, pointX[i], pointY[i], pointZ[i]);
                }
                if (!carved)
                {
                    pointX[kept] = pointX[i];
                    pointY[kept] = pointY[i];
                    pointZ[kept] = pointZ[i];
                    pointEvaluated[kept] = pointEvaluated[i];
                    pointValue[kept] = pointValue[i];
                    ++kept;
                }
            }
            pointCount = kept;
            taken.Clear();
            for (int i = 0; i < pointCount; ++i)
            {
                taken.Add(Cell(pointX[i], pointY[i], pointZ[i]));
            }
            Intersections(first);
            return true;
        }

        private void AddSphere(SdfVector c, double r)
        {
            if (sphereCount == sphereX.Length)
            {
                Array.Resize(ref sphereX, 2 * sphereCount);
                Array.Resize(ref sphereY, 2 * sphereCount);
                Array.Resize(ref sphereZ, 2 * sphereCount);
                Array.Resize(ref sphereR, 2 * sphereCount);
            }
            sphereX[sphereCount] = c.x;
            sphereY[sphereCount] = c.y;
            sphereZ[sphereCount] = c.z;
            sphereR[sphereCount] = r;
            ++sphereCount;
        }

        private void AddPoint(double x, double y, double z)
        {
            if (pointCount == pointX.Length)
            {
                Array.Resize(ref pointX, 2 * pointCount);
                Array.Resize(ref pointY, 2 * pointCount);
                Array.Resize(ref pointZ, 2 * pointCount);
                Array.Resize(ref pointEvaluated, 2 * pointCount);
                Array.Resize(ref pointValue, 2 * pointCount);
            }
            pointX[pointCount] = x;
            pointY[pointCount] = y;
            pointZ[pointCount] = z;
            pointEvaluated[pointCount] = false;
            pointValue[pointCount] = 0;
            ++pointCount;
        }

        // Strictly inside the ball s (a point on a ball is on the border of what is left)
        private bool Inside(int s, double x, double y, double z)
        {
            double dx = x - sphereX[s], dy = y - sphereY[s], dz = z - sphereZ[s];
            double reach = sphereR[s] - 1e-9 * radius;
            return reach > 0 && dx * dx + dy * dy + dz * dz < reach * reach;
        }

        private bool Meet(int a, int b)
        {
            double dx = sphereX[a] - sphereX[b], dy = sphereY[a] - sphereY[b], dz = sphereZ[a] - sphereZ[b];
            double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            return distance < sphereR[a] + sphereR[b] && distance > Math.Abs(sphereR[a] - sphereR[b]);
        }

        // The points where three balls meet, for the triples with a ball from `first` on (the others were done in earlier rounds).
        // A ball that holds a point of the sphere k reaches into the ball k, so only those are looked at for the points on k.
        private void Intersections(int first)
        {
            List<int> touching = new List<int>(), earlier = new List<int>();
            for (int k = first; k < sphereCount; ++k)
            {
                touching.Clear();
                earlier.Clear();
                for (int s = 0; s < sphereCount; ++s)
                {
                    if (s == k)
                    {
                        continue;
                    }
                    double dx = sphereX[s] - sphereX[k], dy = sphereY[s] - sphereY[k], dz = sphereZ[s] - sphereZ[k];
                    double reach = sphereR[s] + sphereR[k];
                    if (dx * dx + dy * dy + dz * dz < reach * reach)
                    {
                        touching.Add(s);
                        if (s < k && Meet(s, k))
                        {
                            earlier.Add(s);
                        }
                    }
                }
                for (int a = 0; a < earlier.Count; ++a)
                {
                    for (int b = a + 1; b < earlier.Count; ++b)
                    {
                        int i = earlier[a], j = earlier[b];
                        if (Meet(i, j))
                        {
                            Trilaterate(i, j, k, touching);
                        }
                    }
                }
            }
        }

        // The two points on the three spheres (the appendix A.2 of the paper), each kept if it is on the border of what is left
        private void Trilaterate(int i, int j, int k, List<int> touching)
        {
            SdfVector c1 = new SdfVector(sphereX[i], sphereY[i], sphereZ[i]), c2 = new SdfVector(sphereX[j], sphereY[j], sphereZ[j]);
            SdfVector c3 = new SdfVector(sphereX[k], sphereY[k], sphereZ[k]);
            double r1 = sphereR[i], r2 = sphereR[j], r3 = sphereR[k];
            SdfVector toSecond = c2 - c1, toThird = c3 - c1;
            double d = toSecond.Length();
            if (d < 1e-12 * radius)
            {
                return;
            }
            SdfVector ex = toSecond / d;
            double along = SdfVector.Dot(ex, toThird);
            SdfVector across = toThird - ex * along;
            double j2 = across.Length();
            if (j2 < 1e-9 * radius)
            {
                // The three centers on a line
                return;
            }
            SdfVector ey = across / j2, ez = SdfVector.Cross(ex, ey);
            double x = (r1 * r1 - r2 * r2 + d * d) / (2 * d);
            double y = (r1 * r1 - r3 * r3 + along * along + j2 * j2) / (2 * j2) - along * x / j2;
            double squared = r1 * r1 - x * x - y * y;
            if (squared < 0)
            {
                return;
            }
            double z = Math.Sqrt(squared);
            SdfVector foot = c1 + ex * x + ey * y;
            Candidate(foot + ez * z, i, j, touching);
            if (z > 0)
            {
                Candidate(foot - ez * z, i, j, touching);
            }
        }

        private void Candidate(SdfVector p, int i, int j, List<int> touching)
        {
            if ((p - center).LengthSquared() > radius * radius)
            {
                return;
            }
            foreach (int s in touching)
            {
                if (s != i && s != j && Inside(s, p.x, p.y, p.z))
                {
                    return;
                }
            }
            // Four balls through one point give it more than once
            if (!taken.Add(Cell(p.x, p.y, p.z)))
            {
                return;
            }
            AddPoint(p.x, p.y, p.z);
        }

        // The cell of a point in a grid of the size below which two points are the same
        private (long, long, long) Cell(double x, double y, double z)
        {
            double size = 1e-5 * radius;
            return ((long)Math.Round((x - center.x) / size), (long)Math.Round((y - center.y) / size), (long)Math.Round((z - center.z) / size));
        }

        // The unit normals of the faces of the convex hull of the points, three numbers each. Empty for points in a plane.
        private List<double> HullNormals()
        {
            List<double> normals = new List<double>();
            List<int> faces = HullFaces(pointX, pointY, pointZ, pointCount, radius);
            for (int f = 0; f < faces.Count; f += 3)
            {
                SdfVector fa = Point(faces[f]);
                SdfVector fn = SdfVector.Cross(Point(faces[f + 1]) - fa, Point(faces[f + 2]) - fa);
                double length = fn.Length();
                if (length > 1e-14 * radius * radius)
                {
                    normals.Add(fn.x / length);
                    normals.Add(fn.y / length);
                    normals.Add(fn.z / length);
                }
            }
            return normals;
        }

        // The volume of the convex hull of `count` points
        private static double HullVolume(double[] x, double[] y, double[] z, int count, double scale)
        {
            List<int> faces = HullFaces(x, y, z, count, scale);
            if (faces.Count == 0)
            {
                return 0;
            }
            SdfVector origin = new SdfVector(x[faces[0]], y[faces[0]], z[faces[0]]);
            double volume = 0;
            for (int f = 0; f < faces.Count; f += 3)
            {
                SdfVector a = new SdfVector(x[faces[f]], y[faces[f]], z[faces[f]]) - origin;
                SdfVector b = new SdfVector(x[faces[f + 1]], y[faces[f + 1]], z[faces[f + 1]]) - origin;
                SdfVector c = new SdfVector(x[faces[f + 2]], y[faces[f + 2]], z[faces[f + 2]]) - origin;
                volume += SdfVector.Dot(a, SdfVector.Cross(b, c));
            }
            return Math.Abs(volume) / 6;
        }

        // The faces of the convex hull of `count` points, three indices each with the normal outward: the hull is built one point
        // at a time, each point replacing the faces that see it by a cone from the point to their border. Empty for points in a
        // plane. scale is the size that the tolerances are relative to.
        private static List<int> HullFaces(double[] x, double[] y, double[] z, int count, double scale)
        {
            List<int> faces = new List<int>();
            if (count < 4)
            {
                return faces;
            }
            // A first tetrahedron: the two ends along x, the point farthest from their line, the point farthest from the plane
            int p0 = 0, p1 = 0;
            for (int i = 0; i < count; ++i)
            {
                if (x[i] < x[p0]) p0 = i;
                if (x[i] > x[p1]) p1 = i;
            }
            SdfVector a = new SdfVector(x[p0], y[p0], z[p0]), ab = new SdfVector(x[p1], y[p1], z[p1]) - a;
            int p2 = -1, p3 = -1;
            double best = 1e-18 * scale * scale * scale * scale;
            for (int i = 0; i < count; ++i)
            {
                double area = SdfVector.Cross(ab, new SdfVector(x[i], y[i], z[i]) - a).LengthSquared();
                if (area > best)
                {
                    best = area;
                    p2 = i;
                }
            }
            if (p2 < 0)
            {
                return faces;
            }
            SdfVector normal = SdfVector.Cross(ab, new SdfVector(x[p2], y[p2], z[p2]) - a).Normalized();
            best = 1e-9 * scale;
            for (int i = 0; i < count; ++i)
            {
                double height = Math.Abs(SdfVector.Dot(normal, new SdfVector(x[i], y[i], z[i]) - a));
                if (height > best)
                {
                    best = height;
                    p3 = i;
                }
            }
            if (p3 < 0)
            {
                return faces;
            }
            SdfVector inner = (a + new SdfVector(x[p1], y[p1], z[p1]) + new SdfVector(x[p2], y[p2], z[p2]) + new SdfVector(x[p3], y[p3], z[p3])) / 4;
            AddFace(faces, x, y, z, p0, p1, p2, inner);
            AddFace(faces, x, y, z, p0, p1, p3, inner);
            AddFace(faces, x, y, z, p0, p2, p3, inner);
            AddFace(faces, x, y, z, p1, p2, p3, inner);
            double tolerance = 1e-10 * scale;
            HashSet<long> edges = new HashSet<long>();
            List<int> visible = new List<int>();
            for (int i = 0; i < count; ++i)
            {
                if (i == p0 || i == p1 || i == p2 || i == p3)
                {
                    continue;
                }
                SdfVector p = new SdfVector(x[i], y[i], z[i]);
                visible.Clear();
                edges.Clear();
                for (int f = 0; f < faces.Count; f += 3)
                {
                    SdfVector fa = new SdfVector(x[faces[f]], y[faces[f]], z[faces[f]]);
                    SdfVector fn = SdfVector.Cross(new SdfVector(x[faces[f + 1]], y[faces[f + 1]], z[faces[f + 1]]) - fa,
                                                   new SdfVector(x[faces[f + 2]], y[faces[f + 2]], z[faces[f + 2]]) - fa);
                    double length = fn.Length();
                    if (length > 0 && SdfVector.Dot(fn, p - fa) > tolerance * length)
                    {
                        visible.Add(f);
                        edges.Add(((long)faces[f] << 32) | (uint)faces[f + 1]);
                        edges.Add(((long)faces[f + 1] << 32) | (uint)faces[f + 2]);
                        edges.Add(((long)faces[f + 2] << 32) | (uint)faces[f]);
                    }
                }
                if (visible.Count == 0)
                {
                    continue;
                }
                List<int> next = new List<int>();
                int v = 0;
                for (int f = 0; f < faces.Count; f += 3)
                {
                    if (v < visible.Count && visible[v] == f)
                    {
                        ++v;
                        // An edge whose other side is not seen is on the border: a new face from it to the point
                        for (int e = 0; e < 3; ++e)
                        {
                            int from = faces[f + e], to = faces[f + (e + 1) % 3];
                            if (!edges.Contains(((long)to << 32) | (uint)from))
                            {
                                next.Add(from);
                                next.Add(to);
                                next.Add(i);
                            }
                        }
                        continue;
                    }
                    next.Add(faces[f]);
                    next.Add(faces[f + 1]);
                    next.Add(faces[f + 2]);
                }
                faces = next;
            }
            return faces;
        }

        // A face with its normal away from the inner point
        private static void AddFace(List<int> faces, double[] x, double[] y, double[] z, int a, int b, int c, SdfVector inner)
        {
            SdfVector pa = new SdfVector(x[a], y[a], z[a]);
            SdfVector pb = new SdfVector(x[b], y[b], z[b]), pc = new SdfVector(x[c], y[c], z[c]);
            bool outward = SdfVector.Dot(SdfVector.Cross(pb - pa, pc - pa), inner - pa) < 0;
            faces.Add(a);
            faces.Add(outward ? b : c);
            faces.Add(outward ? c : b);
        }

        // The approximate convex decomposition of section 5.2: the points are cut in two by the plane, out of a few hundred
        // candidates, whose two halves have the convex hulls of the smallest volume together, and the largest part is cut again
        // until there are `parts` of them (a first cut of a ring gains nothing, the later ones do). The carving is left whole
        // if the pieces together are not smaller than it by a tenth. The hulls of the two halves of the points do not
        // hold the object by themselves; as in the paper the plane is turned into a very large ball on the far side of it, added
        // to the balls, and the points where three balls meet are found again for each side. That needs no value of the field.
        public SdfCarving[] Decompose(int parts)
        {
            List<SdfCarving> pieces = new List<SdfCarving> { this };
            List<double> volumes = new List<double> { HullVolume(pointX, pointY, pointZ, pointCount, radius) };
            List<bool> whole = new List<bool> { false };
            while (pieces.Count < parts)
            {
                int largest = -1;
                for (int i = 0; i < pieces.Count; ++i)
                {
                    if (!whole[i] && (largest < 0 || volumes[i] > volumes[largest]))
                    {
                        largest = i;
                    }
                }
                if (largest < 0)
                {
                    break;
                }
                SdfCarving piece = pieces[largest];
                SdfCarving left = null, right = null;
                if (!double.IsInfinity(piece.BestCut(out SdfVector normal, out double offset)))
                {
                    left = new SdfCarving(piece, normal, offset);
                    right = new SdfCarving(piece, -normal, -offset);
                }
                double leftVolume = left != null && left.valid ? HullVolume(left.pointX, left.pointY, left.pointZ, left.pointCount, radius) : 0;
                double rightVolume = right != null && right.valid ? HullVolume(right.pointX, right.pointY, right.pointZ, right.pointCount, radius) : 0;
                if (leftVolume <= 0 || rightVolume <= 0)
                {
                    // No cut of this part leaves two solid halves
                    whole[largest] = true;
                    continue;
                }
                pieces[largest] = left;
                volumes[largest] = leftVolume;
                pieces.Add(right);
                volumes.Add(rightVolume);
                whole.Add(false);
            }
            double together = 0;
            for (int i = 0; i < pieces.Count; ++i)
            {
                together += volumes[i];
            }
            if (together >= 0.9 * HullVolume(pointX, pointY, pointZ, pointCount, radius))
            {
                return new SdfCarving[] { this };
            }
            return pieces.ToArray();
        }

        // The side n . p < offset of a carving: its balls and a very large ball that touches the plane from the other side, with
        // the points where three of them meet. The ball stands for the half space (the paper sets its radius to that of V0; a
        // larger one bulges less across the plane, so the two sides overlap less).
        private SdfCarving(SdfCarving source, SdfVector normal, double offset)
        {
            field = source.field;
            center = source.center;
            radius = source.radius;
            lipschitz = source.lipschitz;
            for (int s = 0; s < source.sphereCount; ++s)
            {
                AddSphere(new SdfVector(source.sphereX[s], source.sphereY[s], source.sphereZ[s]), source.sphereR[s]);
            }
            // The ball touches the plane below the middle of the points of this side
            SdfVector middle = new SdfVector(0, 0, 0);
            int count = 0;
            for (int i = 0; i < source.pointCount; ++i)
            {
                if (SdfVector.Dot(normal, source.Point(i)) < offset)
                {
                    middle = middle + source.Point(i);
                    ++count;
                }
            }
            if (count == 0)
            {
                valid = false;
                return;
            }
            middle = middle / count;
            SdfVector foot = middle + normal * (offset - SdfVector.Dot(normal, middle));
            double large = 64 * radius;
            AddSphere(foot + normal * large, large);
            Intersections(0);
            valid = pointCount >= 4;
        }

        // The cut of the points with the smallest volume of the hulls of its two halves: the volume, and the plane n . p = offset
        private double BestCut(out SdfVector normal, out double offset)
        {
            // A share of the points is enough to compare cuts
            int stride = Math.Max(1, pointCount / 400), count = 0;
            double[] x = new double[pointCount / stride + 1], y = new double[pointCount / stride + 1], z = new double[pointCount / stride + 1];
            for (int i = 0; i < pointCount; i += stride)
            {
                x[count] = pointX[i];
                y[count] = pointY[i];
                z[count] = pointZ[i];
                ++count;
            }
            double[] lx = new double[count], ly = new double[count], lz = new double[count];
            double[] rx = new double[count], ry = new double[count], rz = new double[count];
            double[] along = new double[count], sorted = new double[count];
            double best = double.PositiveInfinity;
            normal = new SdfVector(1, 0, 0);
            offset = 0;
            // The three axes and directions spread over the half sphere, eight cuts each at even shares of the points
            const int directions = 32;
            for (int d = 0; d < directions + 3; ++d)
            {
                SdfVector n;
                if (d < 3)
                {
                    n = new SdfVector(d == 0 ? 1 : 0, d == 1 ? 1 : 0, d == 2 ? 1 : 0);
                }
                else
                {
                    double height = (d - 3 + 0.5) / directions, turn = 2.39996322972865332 * (d - 3), ring = Math.Sqrt(1 - height * height);
                    n = new SdfVector(ring * Math.Cos(turn), ring * Math.Sin(turn), height);
                }
                for (int i = 0; i < count; ++i)
                {
                    along[i] = n.x * x[i] + n.y * y[i] + n.z * z[i];
                    sorted[i] = along[i];
                }
                Array.Sort(sorted);
                for (int cut = 1; cut <= 8; ++cut)
                {
                    double at = sorted[Math.Min(count - 1, cut * count / 9)];
                    int left = 0, right = 0;
                    for (int i = 0; i < count; ++i)
                    {
                        if (along[i] < at)
                        {
                            lx[left] = x[i]; ly[left] = y[i]; lz[left] = z[i];
                            ++left;
                        }
                        else
                        {
                            rx[right] = x[i]; ry[right] = y[i]; rz[right] = z[i];
                            ++right;
                        }
                    }
                    if (left < 4 || right < 4)
                    {
                        continue;
                    }
                    double volume = HullVolume(lx, ly, lz, left, radius) + HullVolume(rx, ry, rz, right, radius);
                    if (volume > 0 && volume < best)
                    {
                        best = volume;
                        normal = n;
                        offset = at;
                    }
                }
            }
            return best;
        }

        // Carves the field of an expression from the sphere V0 of the given center and radius; a field with a Lipschitz bound
        // above 1 is carved from the smaller sphere of `closer` times the radius instead. The field is parsed for the reach of
        // V0 with a context from newContext, since the bound of a twist or a bend only holds within the radius that it was made
        // for. null if the field is not a proven bound of the distance or the carving did not start.
        public static SdfCarving FromExpression(string expression, Func<SdfParseContext> newContext, SdfVector center, double radius,
                                                double closer, double threshold, int maxSpheres)
        {
            for (int attempt = 0; attempt < 2; ++attempt)
            {
                SdfParseContext context = newContext();
                context.quiet = true;
                context.domainRadius = center.Length() + radius;
                SdfNode field = SdfParser.Parse(expression, context);
                if (!field.IsRigorous())
                {
                    return null;
                }
                if (attempt == 0 && closer < 1 && field.Lipschitz() > 1.0001)
                {
                    radius *= closer;
                    continue;
                }
                SdfCarving carving = new SdfCarving(field, center, radius, threshold, maxSpheres, 64);
                return carving.IsValid() ? carving : null;
            }
            return null;
        }

        // Whether an expression has a part that moves with time, which a carving (made at one time) would not hold
        public static bool MovesWithTime(string expression)
        {
            return expression.Contains("_t(") || expression.Contains("morph(") || expression.Contains("mandelbulbt(");
        }

        // SdfCarving Private Members
        private readonly SdfNode field;
        private readonly SdfVector center;
        private readonly double radius, lipschitz;
        private double[] sphereX = new double[64], sphereY = new double[64], sphereZ = new double[64], sphereR = new double[64];
        private double[] pointX = new double[256], pointY = new double[256], pointZ = new double[256];
        private bool[] pointEvaluated = new bool[256];
        private double[] pointValue = new double[256];
        private readonly HashSet<(long, long, long)> taken = new HashSet<(long, long, long)>();
        private int sphereCount, pointCount, evaluations, iterations;
        private bool valid;
    }

    // SdfCarvedHulls Definition: convex volumes given by planes (four numbers a plane: the unit normal and the offset, n . p <=
    // offset inside), whose union holds a surface
    public static class SdfCarvedHulls
    {
        // A lower bound of the distance of p to the union of the volumes (negative inside one of them)
        public static double Beyond(double[][] hulls, SdfVector p)
        {
            double smallest = double.PositiveInfinity;
            for (int h = 0; h < hulls.Length; ++h)
            {
                double[] planes = hulls[h];
                double largest = double.NegativeInfinity;
                for (int i = 0; i < planes.Length; i += 4)
                {
                    largest = Math.Max(largest, planes[i] * p.x + planes[i + 1] * p.y + planes[i + 2] * p.z - planes[i + 3]);
                }
                smallest = Math.Min(smallest, largest);
            }
            return smallest;
        }

        // The planes of the parts of a carving cut into up to `parts` convex pieces, `planes` planes each besides the six sides
        // of the box of the piece
        public static double[][] FromCarving(SdfCarving carving, int parts, int planes)
        {
            SdfCarving[] pieces = parts > 1 ? carving.Decompose(parts) : new SdfCarving[] { carving };
            double[][] hulls = new double[pieces.Length][];
            for (int i = 0; i < pieces.Length; ++i)
            {
                hulls[i] = pieces[i].Planes(planes);
            }
            return hulls;
        }
    }

    // SdfCarvedField Definition: a field with the cheap proxy of Sphere Carving outside a convex volume given by planes. Beyond
    // the planes by more than epsilon the value is the distance to them (times the Lipschitz bound of the field, since the
    // tracer divides by it), which is a lower bound of the distance to the object inside them; elsewhere it is the field.
    public sealed class SdfCarvedField : SdfNode
    {
        public SdfCarvedField(SdfNode source, double[][] hulls, double epsilon)
        {
            this.source = source;
            this.hulls = hulls;
            this.epsilon = epsilon;
            lipschitz = source.Lipschitz();
        }

        // For each convex volume the largest signed distance to a plane (at most the distance to the volume, negative inside
        // it), and the smallest of these over the volumes: at most the distance to all of them
        private double Beyond(SdfVector p)
        {
            return SdfCarvedHulls.Beyond(hulls, p);
        }

        public override double Evaluate(SdfVector p)
        {
            double beyond = Beyond(p);
            return beyond > epsilon ? beyond * lipschitz : source.Evaluate(p);
        }

        public override double Lipschitz()
        {
            return lipschitz;
        }

        public override double Lipschitz(SdfVector center, double radius)
        {
            // The local bound of the field only where the whole ball is on the side of the field
            return Beyond(center) + radius <= epsilon ? source.Lipschitz(center, radius) : lipschitz;
        }

        public override bool HasLocalBounds()
        {
            return source.HasLocalBounds();
        }

        public override bool IsRigorous()
        {
            return source.IsRigorous();
        }

        public override bool IsExactInside()
        {
            return source.IsExactInside();
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            return source.Bounds(out lower, out upper, out grows);
        }

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            return source.AnalyticGradient(p, h);
        }

        public override void Weights(SdfVector p, SdfWeights w, double weight)
        {
            source.Weights(p, w, weight);
        }

        private readonly SdfNode source;
        private readonly double[][] hulls;
        private readonly double epsilon, lipschitz;
    }
}
