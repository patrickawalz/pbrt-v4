// (not in pbrt-v4) motionblur.cs
//
// Deformation motion blur: a shape that is made once from several keys of its geometry (positions, normals, control points, parameters, each key
// at a time) and intersects, samples and bounds itself as it is at the time of the ray. The transform blur (AnimatedTransform and
// AnimatedPrimitive) is separate and combines with it. See docs/motion-blur.md for the syntax, the strategy of every shape and its cost.
//
//   DeformingTriangle, DeformingBilinearPatch   the vertices are interpolated in the intersection test (a lerp of 3 or 4 points); exact
//   RebuiltShape                                a small shape (a quadric, a particle, a curve segment) is made at the time of the ray; exact
//   SlicedShape                                 the shape is made at a number of times per interval and the slice nearest to the time is used
//                                               (every shape that has no exact method); an approximation of the time, documented

using System;
using System.Collections.Generic;

namespace pbrt
{
    public static class ShapeMotion
    {
        // The number of slices of an interval between two keys that a SlicedShape holds (the slices are at the fractions j / SliceCount, so the
        // keys themselves are slices)
        public const int DefaultSliceCount = 8;

        // Where the time is between the keys: key k and the fraction f from key k to key k + 1 (clamped to the first and the last key). At a key time
        // the fraction is 0 (1 for the last key) so that the interpolation returns the key exactly.
        public static void FindKey(Float[] times, Float time, out int k, out Float f)
        {
            int n = times.Length;
            if (!(time > times[0]))
            {
                k = 0;
                f = 0;
                return;
            }
            if (time >= times[n - 1])
            {
                k = n - 2;
                f = 1;
                return;
            }
            int low = 0, high = n - 1;
            while (high - low > 1)
            {
                int middle = (low + high) / 2;
                if (times[middle] <= time)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }
            k = low;
            f = (time - times[k]) / (times[k + 1] - times[k]);
        }

        // The times at which the bounds and the normal cones of a shape are sampled: SamplesPerInterval equal steps in each interval between the keys
        public static List<Float> SampleTimes(Float[] times, int samplesPerInterval)
        {
            List<Float> result = new List<Float>();
            for (int k = 0; k + 1 < times.Length; ++k)
            {
                for (int j = 0; j < samplesPerInterval; ++j)
                {
                    result.Add(times[k] + (times[k + 1] - times[k]) * j / samplesPerInterval);
                }
            }
            result.Add(times[times.Length - 1]);
            return result;
        }

        // The cone of the normals of a shape over the shutter: the union of the cones at sampled times, opened by the largest angle between two
        // consecutive samples (the normal of the shapes moves smoothly with the time; this is a bound for the motions that are close to linear
        // between the samples, and it is tested by sampling, but it is not a proof)
        public static DirectionCone NormalConeOverTime(Func<Float, Shape> at, Float[] times)
        {
            List<Float> samples = SampleTimes(times, 32);
            DirectionCone cone = DirectionCone.Empty();
            Vector3f previous = default;
            Float step = 0;
            for (int i = 0; i < samples.Count; ++i)
            {
                DirectionCone c = at(samples[i]).NormalBounds();
                if (c.IsEmpty())
                {
                    continue;
                }
                if (i > 0 && c.cosTheta > -1)
                {
                    step = std.max(step, pbrt.AngleBetween(previous, c.w));
                }
                previous = c.w;
                cone = pbrt.Union(cone, c);
            }
            if (cone.IsEmpty() || cone.cosTheta <= -1)
            {
                return cone;
            }
            Float theta = std.acos(Clamp(cone.cosTheta, -1, 1)) + step + 1e-5f;
            if (theta >= Pi)
            {
                return DirectionCone.EntireSphere();
            }
            return new DirectionCone(cone.w, std.cos(theta));
        }

        // The mean of the area over the shutter, from sampled times
        public static Float MeanArea(Func<Float, Shape> at, Float[] times)
        {
            List<Float> samples = SampleTimes(times, 16);
            double sum = 0;
            foreach (Float t in samples)
            {
                sum += at(t).Area();
            }
            return (Float)(sum / samples.Count);
        }

        // The bounds of a shape over the shutter from the bounds at sampled times, opened by the largest change of the box between two samples
        public static Bounds3f BoundsOverTime(Func<Float, Shape> at, Float[] times)
        {
            List<Float> samples = SampleTimes(times, 64);
            Bounds3f bounds = at(samples[0]).Bounds();
            Bounds3f previous = bounds;
            Float change = 0;
            for (int i = 1; i < samples.Count; ++i)
            {
                Bounds3f b = at(samples[i]).Bounds();
                for (int a = 0; a < 3; ++a)
                {
                    change = std.max(change, std.abs(b.pMin[a] - previous.pMin[a]));
                    change = std.max(change, std.abs(b.pMax[a] - previous.pMax[a]));
                }
                bounds = pbrt.Union(bounds, b);
                previous = b;
            }
            return pbrt.Expand(bounds, change + 1e-6f * (std.abs(bounds.pMax.x) + std.abs(bounds.pMax.y) + std.abs(bounds.pMax.z) + 1));
        }

        // The key lists of two meshes hold the same values
        private static bool SameValues(Point3f[] a, Point3f[] b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }
            if (a.Length != b.Length)
            {
                return false;
            }
            for (int i = 0; i < a.Length; ++i)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static bool SameValues(Normal3f[] a, Normal3f[] b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }
            for (int i = 0; i < a.Length; ++i)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static bool SameIndices(int[] a, int[] b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }
            for (int i = 0; i < a.Length; ++i)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }

        // The deforming shapes of the keys when every key made triangles and bilinear patches (meshes): the topology (indices, uv, face indices)
        // is that of the first key and must be the same in all of them; the positions and the normals are the keys. A mesh whose keys are all equal
        // stays the static mesh of the first key.
        private static pstd.vector<Shape> MergeMeshes(string name, pstd.vector<Shape>[] perKey, Float[] times, FileLoc loc)
        {
            int count = (int)perKey[0].size();
            pstd.vector<Shape> result = new pstd.vector<Shape>((ulong)count);
            Dictionary<TriangleMesh, DeformingTriangleMesh> triangleGroups = new Dictionary<TriangleMesh, DeformingTriangleMesh>();
            Dictionary<BilinearPatchMesh, DeformingBilinearPatchMesh> patchGroups = new Dictionary<BilinearPatchMesh, DeformingBilinearPatchMesh>();
            for (int i = 0; i < count; ++i)
            {
                Triangle t0 = perKey[0][i] as Triangle;
                BilinearPatch b0 = perKey[0][i] as BilinearPatch;
                if (t0 != null)
                {
                    TriangleMesh[] meshes = new TriangleMesh[times.Length];
                    for (int k = 0; k < times.Length; ++k)
                    {
                        Triangle tk = perKey[k][i] as Triangle;
                        if (tk == null || tk.TriangleIndex() != t0.TriangleIndex())
                        {
                            pbrt.ErrorExit(loc, name + ": key " + k + " of the deforming shape does not have the same triangles as the first key.");
                        }
                        meshes[k] = tk.Mesh();
                    }
                    DeformingTriangleMesh group;
                    if (!triangleGroups.TryGetValue(meshes[0], out group))
                    {
                        group = DeformingTriangleMesh.Make(name, meshes, times, loc);
                        triangleGroups[meshes[0]] = group;
                    }
                    result[i] = group == null ? perKey[0][i] : new DeformingTriangle(group, t0.TriangleIndex());
                }
                else if (b0 != null)
                {
                    BilinearPatchMesh[] meshes = new BilinearPatchMesh[times.Length];
                    for (int k = 0; k < times.Length; ++k)
                    {
                        BilinearPatch bk = perKey[k][i] as BilinearPatch;
                        if (bk == null || bk.PatchIndex() != b0.PatchIndex())
                        {
                            pbrt.ErrorExit(loc, name + ": key " + k + " of the deforming shape does not have the same patches as the first key.");
                        }
                        meshes[k] = bk.Mesh();
                    }
                    DeformingBilinearPatchMesh group;
                    if (!patchGroups.TryGetValue(meshes[0], out group))
                    {
                        group = DeformingBilinearPatchMesh.Make(name, meshes, times, loc);
                        patchGroups[meshes[0]] = group;
                    }
                    result[i] = group == null ? perKey[0][i] : new DeformingBilinearPatch(group, b0.PatchIndex());
                }
                else
                {
                    pbrt.ErrorExit(loc, name + ": internal error, a mesh shape was expected.");
                }
            }
            return result;
        }

        internal static bool EqualPositions(Point3f[] a, Point3f[] b)
        {
            return SameValues(a, b);
        }

        internal static bool EqualNormals(Normal3f[] a, Normal3f[] b)
        {
            return SameValues(a, b);
        }

        internal static bool EqualIndices(int[] a, int[] b)
        {
            return SameIndices(a, b);
        }

        private static bool AllMeshes(pstd.vector<Shape> shapes)
        {
            if (shapes.size() == 0)
            {
                return false;
            }
            for (ulong i = 0; i < shapes.size(); ++i)
            {
                if (!(shapes[i] is Triangle) && !(shapes[i] is BilinearPatch))
                {
                    return false;
                }
            }
            return true;
        }

        // Shape.Create for a shape with several keys: parameters[k] are the parameters of the key k at times[k]. renderFromObject is the one of the
        // shape (the same at every key; the animated transformation of the shape is a separate matter, see AnimatedPrimitive).
        public static pstd.vector<Shape> Create(string name, Transform renderFromObject, Transform objectFromRender, bool reverseOrientation,
                                                ParameterDictionary[] parameters, Float[] times, IDictionary<string, FloatTexture> floatTextures,
                                                FileLoc loc, Allocator alloc)
        {
            if (parameters.Length != times.Length || times.Length < 2)
            {
                pbrt.ErrorExit(loc, name + ": a deforming shape needs one set of parameters for each of at least two times.");
            }
            string problem = ParameterDictionary.CheckKeys(parameters);
            if (problem != null)
            {
                pbrt.ErrorExit(loc, name + ": the keys of a deforming shape must have the same parameters: " + problem + ".");
            }
            pstd.vector<Shape>[] perKey = new pstd.vector<Shape>[times.Length];
            for (int k = 0; k < times.Length; ++k)
            {
                perKey[k] = Shape.Create(name, renderFromObject, objectFromRender, reverseOrientation, parameters[k], floatTextures, loc, alloc);
                if (perKey[k].size() != perKey[0].size())
                {
                    pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: key %d of the deforming shape made %d shapes and the first key %d. The keys must have the same topology " +
                                                          "(the same counts and indices). For a displaced (diced) shape the number of micropolygons follows the size of the shape: give it \"dicemaxrate\" and a " +
                                                          "small \"edgelength\" so that every key is diced at the same rate.", name, k, (int)perKey[k].size(), (int)perKey[0].size()));
                }
            }
            if (AllMeshes(perKey[0]))
            {
                return MergeMeshes(name, perKey, times, loc);
            }
            pstd.vector<Shape> exact = ExactMotion.TryCreate(name, renderFromObject, objectFromRender, reverseOrientation, parameters, times, perKey, loc);
            if (exact != null)
            {
                return exact;
            }
            return SlicedShape.CreateSlices(name, renderFromObject, objectFromRender, reverseOrientation, parameters, times, floatTextures, loc, alloc, perKey[0].size());
        }
    }

    // ----------------------------------------------------------------------------------------------------------------------------------------
    // Triangle meshes

    public sealed unsafe class DeformingTriangleMesh
    {
        public TriangleMesh first;
        public Float[] times;
        public Point3f[][] p;
        public Normal3f[][] n;   // null without normals

        // The deforming mesh of the meshes of the keys; null if every key has the same positions and normals (the mesh does not move). The topology
        // is checked to be the same.
        public static DeformingTriangleMesh Make(string name, TriangleMesh[] meshes, Float[] times, FileLoc loc)
        {
            bool moves = false;
            for (int k = 0; k < meshes.Length; ++k)
            {
                if (meshes[k].nVertices != meshes[0].nVertices || meshes[k].nTriangles != meshes[0].nTriangles ||
                    !ShapeMotion.EqualIndices(meshes[k].vertexIndices, meshes[0].vertexIndices) || (meshes[k].n == null) != (meshes[0].n == null))
                {
                    pbrt.ErrorExit(loc, name + ": key " + k + " of the deforming mesh has other vertex counts, indices or normals than the first key.");
                }
                if (k > 0 && (!ShapeMotion.EqualPositions(meshes[k].p, meshes[0].p) || (meshes[0].n != null && !ShapeMotion.EqualNormals(meshes[k].n, meshes[0].n))))
                {
                    moves = true;
                }
            }
            if (!moves)
            {
                return null;
            }
            DeformingTriangleMesh result = new DeformingTriangleMesh();
            result.first = meshes[0];
            result.times = times;
            result.p = new Point3f[meshes.Length][];
            result.n = meshes[0].n != null ? new Normal3f[meshes.Length][] : null;
            for (int k = 0; k < meshes.Length; ++k)
            {
                result.p[k] = meshes[k].p;
                if (result.n != null)
                {
                    result.n[k] = meshes[k].n;
                }
            }
            return result;
        }

        // The mesh of the single triangle that the triangle is between key k and key k + 1 at the fraction f (the other data are those of the
        // first key)
        public TriangleMesh Slice(int triIndex, Point3f p0, Point3f p1, Point3f p2, int k, Float f)
        {
            int v0 = first.vertexIndices[3 * triIndex], v1 = first.vertexIndices[3 * triIndex + 1], v2 = first.vertexIndices[3 * triIndex + 2];
            TriangleMesh m = new TriangleMesh();
            m.nTriangles = 1;
            m.nVertices = 3;
            m.vertexIndices = new int[] { 0, 1, 2 };
            m.p = new Point3f[] { p0, p1, p2 };
            if (n != null)
            {
                m.n = new Normal3f[] { pbrt.Lerp(f, n[k][v0], n[k + 1][v0]), pbrt.Lerp(f, n[k][v1], n[k + 1][v1]), pbrt.Lerp(f, n[k][v2], n[k + 1][v2]) };
            }
            if (first.s != null)
            {
                m.s = new Vector3f[] { first.s[v0], first.s[v1], first.s[v2] };
            }
            if (first.uv != null)
            {
                m.uv = new Point2f[] { first.uv[v0], first.uv[v1], first.uv[v2] };
            }
            if (first.faceIndices != null)
            {
                m.faceIndices = new int[] { first.faceIndices[triIndex] };
            }
            m.reverseOrientation = first.reverseOrientation;
            m.transformSwapsHandedness = first.transformSwapsHandedness;
            return m;
        }
    }

    // A triangle of a mesh whose vertices (and normals) have several keys. The intersection test interpolates the three vertices at the time of the
    // ray; the interaction of a hit is computed by the code of Triangle on a one triangle mesh made for the hit.
    public sealed unsafe class DeformingTriangle : Shape
    {
        DeformingTriangleMesh mesh;
        int triIndex;

        public DeformingTriangle(DeformingTriangleMesh mesh, int triIndex)
        {
            this.mesh = mesh;
            this.triIndex = triIndex;
        }

        public override bool IsDeforming()
        {
            return true;
        }

        void Vertices(Float time, out int k, out Float f, out Point3f p0, out Point3f p1, out Point3f p2)
        {
            ShapeMotion.FindKey(mesh.times, time, out k, out f);
            int[] vi = mesh.first.vertexIndices;
            int v0 = vi[3 * triIndex], v1 = vi[3 * triIndex + 1], v2 = vi[3 * triIndex + 2];
            Point3f[] a = mesh.p[k], b = mesh.p[k + 1];
            p0 = pbrt.Lerp(f, a[v0], b[v0]);
            p1 = pbrt.Lerp(f, a[v1], b[v1]);
            p2 = pbrt.Lerp(f, a[v2], b[v2]);
        }

        public override Shape AtTime(Float time)
        {
            int k;
            Float f;
            Point3f p0, p1, p2;
            Vertices(time, out k, out f, out p0, out p1, out p2);
            return new Triangle(mesh.Slice(triIndex, p0, p1, p2, k, f), 0);
        }

        public override Bounds3f Bounds()
        {
            int[] vi = mesh.first.vertexIndices;
            int v0 = vi[3 * triIndex], v1 = vi[3 * triIndex + 1], v2 = vi[3 * triIndex + 2];
            Bounds3f bounds = new Bounds3f(mesh.p[0][v0], mesh.p[0][v1]);
            bounds = pbrt.Union(bounds, mesh.p[0][v2]);
            for (int k = 1; k < mesh.times.Length; ++k)
            {
                bounds = pbrt.Union(bounds, mesh.p[k][v0]);
                bounds = pbrt.Union(bounds, mesh.p[k][v1]);
                bounds = pbrt.Union(bounds, mesh.p[k][v2]);
            }
            return bounds;
        }

        public override DirectionCone NormalBounds()
        {
            return ShapeMotion.NormalConeOverTime(AtTime, mesh.times);
        }

        public override pstd.optional<ShapeIntersection> Intersect(Ray ray, Float tMax = Infinity)
        {
            int k;
            Float f;
            Point3f p0, p1, p2;
            Vertices(ray.time, out k, out f, out p0, out p1, out p2);
            pstd.optional<TriangleIntersection> ti = pbrt.IntersectTriangle(ray, tMax, p0, p1, p2);
            if (!ti)
            {
                return default;
            }
            SurfaceInteraction intr = Triangle.InteractionFromIntersection(mesh.Slice(triIndex, p0, p1, p2, k, f), 0, ti.value(), ray.time, -ray.d);
            return new ShapeIntersection(intr, ti.value().t);
        }

        public override bool IntersectP(Ray ray, Float tMax = Infinity)
        {
            int k;
            Float f;
            Point3f p0, p1, p2;
            Vertices(ray.time, out k, out f, out p0, out p1, out p2);
            return pbrt.IntersectTriangle(ray, tMax, p0, p1, p2).has_value();
        }

        // The area, and the sampling without a time, are those of the first key
        public override Float Area()
        {
            return AtTime(mesh.times[0]).Area();
        }

        public override Float MeanArea()
        {
            return ShapeMotion.MeanArea(AtTime, mesh.times);
        }

        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            return AtTime(mesh.times[0]).Sample(u);
        }

        public override Float PDF(Interaction intr)
        {
            return AtTime(intr.time).PDF(intr);
        }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            return AtTime(ctx.time).Sample(ctx, u);
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            return AtTime(ctx.time).PDF(ctx, wi);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ DeformingTriangle triIndex: %d keys: %d ]", triIndex, mesh.times.Length);
        }
    }

    // ----------------------------------------------------------------------------------------------------------------------------------------
    // Bilinear patch meshes

    public sealed unsafe class DeformingBilinearPatchMesh
    {
        public BilinearPatchMesh first;
        public Float[] times;
        public Point3f[][] p;
        public Normal3f[][] n;

        public static DeformingBilinearPatchMesh Make(string name, BilinearPatchMesh[] meshes, Float[] times, FileLoc loc)
        {
            bool moves = false;
            for (int k = 0; k < meshes.Length; ++k)
            {
                if (meshes[k].nVertices != meshes[0].nVertices || meshes[k].nPatches != meshes[0].nPatches ||
                    !ShapeMotion.EqualIndices(meshes[k].vertexIndices, meshes[0].vertexIndices) || (meshes[k].n == null) != (meshes[0].n == null))
                {
                    pbrt.ErrorExit(loc, name + ": key " + k + " of the deforming mesh has other vertex counts, indices or normals than the first key.");
                }
                if (k > 0 && (!ShapeMotion.EqualPositions(meshes[k].p, meshes[0].p) || (meshes[0].n != null && !ShapeMotion.EqualNormals(meshes[k].n, meshes[0].n))))
                {
                    moves = true;
                }
            }
            if (!moves)
            {
                return null;
            }
            DeformingBilinearPatchMesh result = new DeformingBilinearPatchMesh();
            result.first = meshes[0];
            result.times = times;
            result.p = new Point3f[meshes.Length][];
            result.n = meshes[0].n != null ? new Normal3f[meshes.Length][] : null;
            for (int k = 0; k < meshes.Length; ++k)
            {
                result.p[k] = meshes[k].p;
                if (result.n != null)
                {
                    result.n[k] = meshes[k].n;
                }
            }
            return result;
        }

        public BilinearPatchMesh Slice(int blpIndex, Point3f p00, Point3f p10, Point3f p01, Point3f p11, int k, Float f)
        {
            int vi = 4 * blpIndex;
            int v0 = first.vertexIndices[vi], v1 = first.vertexIndices[vi + 1], v2 = first.vertexIndices[vi + 2], v3 = first.vertexIndices[vi + 3];
            BilinearPatchMesh m = new BilinearPatchMesh();
            m.nPatches = 1;
            m.nVertices = 4;
            m.vertexIndices = new int[] { 0, 1, 2, 3 };
            m.p = new Point3f[] { p00, p10, p01, p11 };
            if (n != null)
            {
                m.n = new Normal3f[] { pbrt.Lerp(f, n[k][v0], n[k + 1][v0]), pbrt.Lerp(f, n[k][v1], n[k + 1][v1]), pbrt.Lerp(f, n[k][v2], n[k + 1][v2]),
                                       pbrt.Lerp(f, n[k][v3], n[k + 1][v3]) };
            }
            if (first.uv != null)
            {
                m.uv = new Point2f[] { first.uv[v0], first.uv[v1], first.uv[v2], first.uv[v3] };
            }
            if (first.faceIndices != null)
            {
                m.faceIndices = new int[] { first.faceIndices[blpIndex] };
            }
            m.imageDistribution = first.imageDistribution;
            m.reverseOrientation = first.reverseOrientation;
            m.transformSwapsHandedness = first.transformSwapsHandedness;
            return m;
        }
    }

    public sealed unsafe class DeformingBilinearPatch : Shape
    {
        DeformingBilinearPatchMesh mesh;
        int blpIndex;

        public DeformingBilinearPatch(DeformingBilinearPatchMesh mesh, int blpIndex)
        {
            this.mesh = mesh;
            this.blpIndex = blpIndex;
        }

        public override bool IsDeforming()
        {
            return true;
        }

        void Vertices(Float time, out int k, out Float f, out Point3f p00, out Point3f p10, out Point3f p01, out Point3f p11)
        {
            ShapeMotion.FindKey(mesh.times, time, out k, out f);
            int[] vi = mesh.first.vertexIndices;
            int v0 = vi[4 * blpIndex], v1 = vi[4 * blpIndex + 1], v2 = vi[4 * blpIndex + 2], v3 = vi[4 * blpIndex + 3];
            Point3f[] a = mesh.p[k], b = mesh.p[k + 1];
            p00 = pbrt.Lerp(f, a[v0], b[v0]);
            p10 = pbrt.Lerp(f, a[v1], b[v1]);
            p01 = pbrt.Lerp(f, a[v2], b[v2]);
            p11 = pbrt.Lerp(f, a[v3], b[v3]);
        }

        public override Shape AtTime(Float time)
        {
            int k;
            Float f;
            Point3f p00, p10, p01, p11;
            Vertices(time, out k, out f, out p00, out p10, out p01, out p11);
            return new BilinearPatch(mesh.Slice(blpIndex, p00, p10, p01, p11, k, f), 0);
        }

        public override Bounds3f Bounds()
        {
            int[] vi = mesh.first.vertexIndices;
            Bounds3f bounds = new Bounds3f(mesh.p[0][vi[4 * blpIndex]], mesh.p[0][vi[4 * blpIndex + 1]]);
            for (int k = 0; k < mesh.times.Length; ++k)
            {
                for (int c = 0; c < 4; ++c)
                {
                    bounds = pbrt.Union(bounds, mesh.p[k][vi[4 * blpIndex + c]]);
                }
            }
            return bounds;
        }

        public override DirectionCone NormalBounds()
        {
            return ShapeMotion.NormalConeOverTime(AtTime, mesh.times);
        }

        public override pstd.optional<ShapeIntersection> Intersect(Ray ray, Float tMax = Infinity)
        {
            int k;
            Float f;
            Point3f p00, p10, p01, p11;
            Vertices(ray.time, out k, out f, out p00, out p10, out p01, out p11);
            pstd.optional<BilinearIntersection> hit = pbrt.IntersectBilinearPatch(ray, tMax, p00, p10, p01, p11);
            if (!hit)
            {
                return default;
            }
            SurfaceInteraction intr = BilinearPatch.InteractionFromIntersection(mesh.Slice(blpIndex, p00, p10, p01, p11, k, f), 0, hit.value().uv, ray.time, -ray.d);
            return new ShapeIntersection(intr, hit.value().t);
        }

        public override bool IntersectP(Ray ray, Float tMax = Infinity)
        {
            int k;
            Float f;
            Point3f p00, p10, p01, p11;
            Vertices(ray.time, out k, out f, out p00, out p10, out p01, out p11);
            return pbrt.IntersectBilinearPatch(ray, tMax, p00, p10, p01, p11).has_value();
        }

        public override Float Area()
        {
            return AtTime(mesh.times[0]).Area();
        }

        public override Float MeanArea()
        {
            return ShapeMotion.MeanArea(AtTime, mesh.times);
        }

        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            return AtTime(mesh.times[0]).Sample(u);
        }

        public override Float PDF(Interaction intr)
        {
            return AtTime(intr.time).PDF(intr);
        }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            return AtTime(ctx.time).Sample(ctx, u);
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            return AtTime(ctx.time).PDF(ctx, wi);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ DeformingBilinearPatch blpIndex: %d keys: %d ]", blpIndex, mesh.times.Length);
        }
    }

    // ----------------------------------------------------------------------------------------------------------------------------------------
    // Shapes that are made for the time of the ray or for a number of times

    // The common part: a shape that is a different static shape at every time. The functions with a time are those of the static shape at that time;
    // the functions without one use the first time.
    public abstract unsafe class TimeVaryingShape : Shape
    {
        protected Float[] times;
        protected Bounds3f bounds;
        protected DirectionCone normalBounds;

        protected abstract Shape At(Float time);

        public override bool IsDeforming()
        {
            return true;
        }

        public override Shape AtTime(Float time)
        {
            return At(time);
        }

        public override Bounds3f Bounds()
        {
            return bounds;
        }

        public override DirectionCone NormalBounds()
        {
            return normalBounds;
        }

        public override pstd.optional<ShapeIntersection> Intersect(Ray ray, Float tMax = Infinity)
        {
            return At(ray.time).Intersect(ray, tMax);
        }

        public override bool IntersectP(Ray ray, Float tMax = Infinity)
        {
            return At(ray.time).IntersectP(ray, tMax);
        }

        public override Float Area()
        {
            return At(times[0]).Area();
        }

        public override Float MeanArea()
        {
            return ShapeMotion.MeanArea(At, times);
        }

        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            return At(times[0]).Sample(u);
        }

        public override Float PDF(Interaction intr)
        {
            return At(intr.time).PDF(intr);
        }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            return At(ctx.time).Sample(ctx, u);
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            return At(ctx.time).PDF(ctx, wi);
        }
    }

    // A shape that is made at the time of the ray from the keys of its (few) numbers: the quadrics, the particles of points, a segment of a curve. The
    // constructor of the static shape is cheap (a few numbers and a transformation), so the shape is exact at every time.
    public sealed unsafe class RebuiltShape : TimeVaryingShape
    {
        Func<Float, Shape> build;

        public RebuiltShape(Func<Float, Shape> build, Float[] times)
        {
            this.build = build;
            this.times = times;
            bounds = ShapeMotion.BoundsOverTime(build, times);
            normalBounds = ShapeMotion.NormalConeOverTime(build, times);
        }

        // With bounds that the caller knows (for example the union of the bounds of the keys when the bounds are linear in the keys)
        public RebuiltShape(Func<Float, Shape> build, Float[] times, Bounds3f bounds, DirectionCone normalBounds)
        {
            this.build = build;
            this.times = times;
            this.bounds = bounds;
            this.normalBounds = normalBounds;
        }

        protected override Shape At(Float time)
        {
            return build(time);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ RebuiltShape keys: %d ]", times.Length);
        }
    }

    // A shape that is made at a number of times per interval between the keys (including the keys); the time of a ray picks the nearest slice.
    // The motion is quantized to SliceCount steps per interval: the blur is the average of that many positions. Every shape has this method.
    public sealed unsafe class SlicedShape : TimeVaryingShape
    {
        Shape[] slices;
        int sliceCount;

        public SlicedShape(Shape[] slices, Float[] times, int sliceCount)
        {
            this.slices = slices;
            this.times = times;
            this.sliceCount = sliceCount;
            Bounds3f b = slices[0].Bounds();
            DirectionCone cone = DirectionCone.Empty();
            for (int i = 0; i < slices.Length; ++i)
            {
                b = pbrt.Union(b, slices[i].Bounds());
                cone = pbrt.Union(cone, slices[i].NormalBounds());
            }
            bounds = b;
            normalBounds = cone;
        }

        public int SliceIndex(Float time)
        {
            int k;
            Float f;
            ShapeMotion.FindKey(times, time, out k, out f);
            int j = (int)(f * sliceCount + 0.5f);
            return k * sliceCount + j;
        }

        protected override Shape At(Float time)
        {
            return slices[SliceIndex(time)];
        }

        // The time of slice i: a key or a fraction j / SliceCount of an interval
        public static Float SliceTime(Float[] times, int sliceCount, int index)
        {
            int k = index / sliceCount, j = index % sliceCount;
            if (k >= times.Length - 1)
            {
                return times[times.Length - 1];
            }
            return times[k] + (times[k + 1] - times[k]) * j / sliceCount;
        }

        // The shapes of the keys with the parameters of the slices: shape i of every slice is one SlicedShape
        public static pstd.vector<Shape> CreateSlices(string name, Transform renderFromObject, Transform objectFromRender, bool reverseOrientation,
                                                      ParameterDictionary[] parameters, Float[] times, IDictionary<string, FloatTexture> floatTextures,
                                                      FileLoc loc, Allocator alloc, ulong count)
        {
            int sliceCount = ShapeMotion.DefaultSliceCount;
            int total = (times.Length - 1) * sliceCount + 1;
            pstd.vector<Shape>[] made = new pstd.vector<Shape>[total];
            for (int s = 0; s < total; ++s)
            {
                Float time = SliceTime(times, sliceCount, s);
                ParameterDictionary at = ParameterDictionary.Interpolate(parameters, times, time);
                made[s] = Shape.Create(name, renderFromObject, objectFromRender, reverseOrientation, at, floatTextures, loc, alloc);
                if (made[s].size() != count)
                {
                    pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: the shape made %d shapes at time %f and %d at the first key. The keys must have the same topology (for a displaced shape: the same dicing rates, see \"dicemaxrate\").",
                                                          name, (int)made[s].size(), time, (int)count));
                }
            }
            pstd.vector<Shape> result = new pstd.vector<Shape>(count);
            for (ulong i = 0; i < count; ++i)
            {
                Shape[] slices = new Shape[total];
                for (int s = 0; s < total; ++s)
                {
                    slices[s] = made[s][i];
                }
                result[i] = new SlicedShape(slices, times, sliceCount);
            }
            return result;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ SlicedShape keys: %d slices: %d ]", times.Length, slices.Length);
        }
    }
}  // namespace pbrt
