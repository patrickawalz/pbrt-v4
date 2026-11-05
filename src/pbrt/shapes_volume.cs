// (not in pbrt-v4) shapes_volume.cs
//
// RiVolume: the region of space of a volume as a shape, so that a MediumInterface can be attached to it. The region is the box
// "bound" [xmin xmax ymin ymax zmin zmax] (RenderMan's order) in the object space; the shape is the surface of that box, intersected exactly with the slab
// method, transformed by the transform of the shape (an affine map takes the box to a parallelepiped, whose faces are parallelograms).
//
//   Shape "volume" "float bound" [...] ("string type" "box")     (RenderMan: RiVolume "box" bound res; the resolution is not used here)
//   Shape "volume" "point3 p0" ... "point3 p1" ...                (pbrt-style corners instead of "bound")
//
// Sampling is exactly uniform in area (a face with the probability of its area, then uniform on the parallelogram), the solid angle density follows from
// the first hit. The normals point outward; (u, v) is the position on the face in [0, 1]^2, with dP/du x dP/dv along the outward normal.

using System;
using System.Collections.Generic;

namespace pbrt
{
    public sealed unsafe class VolumeBox : Shape
    {
        private readonly double[] min = new double[3], max = new double[3];
        private readonly Transform renderFromObject, objectFromRender;
        private readonly bool reverseOrientation, transformSwapsHandedness;
        private readonly Bounds3f bounds;
        private readonly double[] faceArea = new double[6];
        private readonly double[] faceCumulative = new double[6];
        private readonly double area;

        public VolumeBox(double[] low, double[] high, Transform renderFromObject, Transform objectFromRender, bool reverseOrientation)
        {
            for (int a = 0; a < 3; ++a)
            {
                min[a] = low[a];
                max[a] = high[a];
            }
            this.renderFromObject = renderFromObject;
            this.objectFromRender = objectFromRender;
            this.reverseOrientation = reverseOrientation;
            transformSwapsHandedness = renderFromObject.SwapsHandedness();
            Bounds3f result = new Bounds3f();
            for (int corner = 0; corner < 8; ++corner)
            {
                Point3f p = renderFromObject.Apply(new Point3f((Float)((corner & 1) != 0 ? max[0] : min[0]), (Float)((corner & 2) != 0 ? max[1] : min[1]),
                                                               (Float)((corner & 4) != 0 ? max[2] : min[2])));
                result = corner == 0 ? new Bounds3f(p) : pbrt.Union(result, p);
            }
            bounds = result;
            double running = 0;
            for (int face = 0; face < 6; ++face)
            {
                double[] pu, pv;
                FaceFrame(face, out double[] origin, out pu, out pv);
                Vector3f a = renderFromObject.Apply(new Vector3f((Float)pu[0], (Float)pu[1], (Float)pu[2]));
                Vector3f b = renderFromObject.Apply(new Vector3f((Float)pv[0], (Float)pv[1], (Float)pv[2]));
                Vector3f c = pbrt.Cross(a, b);
                faceArea[face] = Math.Sqrt((double)c.x * c.x + (double)c.y * c.y + (double)c.z * c.z);
                running += faceArea[face];
                faceCumulative[face] = running;
            }
            area = running;
        }

        // Face f = 2 a + s: axis a, the minimum (s = 0) or maximum (s = 1) side. The corner of the face where (u, v) = (0, 0) and the edges dP/du, dP/dv
        // (the whole edges), ordered so that dP/du x dP/dv is the outward normal
        private void FaceFrame(int face, out double[] origin, out double[] pu, out double[] pv)
        {
            int a = face >> 1, s = face & 1;
            int a1 = (a + 1) % 3, a2 = (a + 2) % 3;
            origin = new double[3];
            origin[a] = s == 1 ? max[a] : min[a];
            origin[a1] = min[a1];
            origin[a2] = min[a2];
            double[] e1 = new double[3], e2 = new double[3];
            e1[a1] = max[a1] - min[a1];
            e2[a2] = max[a2] - min[a2];
            if (s == 1)
            {
                pu = e1;
                pv = e2;
            }
            else
            {
                pu = e2;
                pv = e1;
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

        // The first t in (0, tMax) of the box in the object space; face is the face that is hit
        private bool Slab(Ray ray, double tMax, out double tHit, out int face)
        {
            tHit = 0;
            face = 0;
            Point3f o = objectFromRender.Apply(ray.o);
            Vector3f d = objectFromRender.Apply(ray.d);
            double[] oo = { o.x, o.y, o.z }, dd = { d.x, d.y, d.z };
            double tNear = double.NegativeInfinity, tFar = double.PositiveInfinity;
            int nearFace = -1, farFace = -1;
            for (int a = 0; a < 3; ++a)
            {
                if (dd[a] == 0)
                {
                    if (oo[a] < min[a] || oo[a] > max[a])
                    {
                        return false;
                    }
                    continue;
                }
                double inv = 1 / dd[a];
                double t0 = (min[a] - oo[a]) * inv, t1 = (max[a] - oo[a]) * inv;
                int f0 = 2 * a, f1 = 2 * a + 1;
                if (t0 > t1)
                {
                    double tmp = t0;
                    t0 = t1;
                    t1 = tmp;
                    f0 = 2 * a + 1;
                    f1 = 2 * a;
                }
                if (t0 > tNear)
                {
                    tNear = t0;
                    nearFace = f0;
                }
                if (t1 < tFar)
                {
                    tFar = t1;
                    farFace = f1;
                }
                if (tNear > tFar)
                {
                    return false;
                }
            }
            if (tNear > 0 && tNear < tMax)
            {
                tHit = tNear;
                face = nearFace;
                return nearFace >= 0;
            }
            if (tFar > 0 && tFar < tMax)
            {
                tHit = tFar;
                face = farFace;
                return farFace >= 0;
            }
            return false;
        }

        public override pstd.optional<ShapeIntersection> Intersect(Ray ray, Float tMax = Infinity)
        {
            if (!Slab(ray, tMax, out double t, out int face))
            {
                return default;
            }
            Point3f o = objectFromRender.Apply(ray.o);
            Vector3f d = objectFromRender.Apply(ray.d);
            double[] p = { o.x + t * d.x, o.y + t * d.y, o.z + t * d.z };
            return new ShapeIntersection(InteractionFromPoint(p, face, -ray.d, ray.time), (Float)t);
        }

        public override bool IntersectP(Ray ray, Float tMax = Infinity)
        {
            return Slab(ray, tMax, out double t, out int face);
        }

        private void Local(double[] p, int face, out Point2f uv, out double[] pu, out double[] pv)
        {
            FaceFrame(face, out double[] origin, out pu, out pv);
            int a = face >> 1, s = face & 1;
            int a1 = (a + 1) % 3, a2 = (a + 2) % 3;
            double x1 = (p[a1] - min[a1]) / (max[a1] - min[a1]), x2 = (p[a2] - min[a2]) / (max[a2] - min[a2]);
            x1 = Math.Min(1, Math.Max(0, x1));
            x2 = Math.Min(1, Math.Max(0, x2));
            uv = s == 1 ? new Point2f((Float)x1, (Float)x2) : new Point2f((Float)x2, (Float)x1);
        }

        private SurfaceInteraction InteractionFromPoint(double[] p, int face, Vector3f wo, Float time)
        {
            Local(p, face, out Point2f uv, out double[] pu, out double[] pv);
            Point3f pr = renderFromObject.Apply(new Point3f((Float)p[0], (Float)p[1], (Float)p[2]));
            Vector3f dpdu = renderFromObject.Apply(new Vector3f((Float)pu[0], (Float)pu[1], (Float)pu[2]));
            Vector3f dpdv = renderFromObject.Apply(new Vector3f((Float)pv[0], (Float)pv[1], (Float)pv[2]));
            bool flipNormal = reverseOrientation ^ transformSwapsHandedness;
            Vector3f pError = pbrt.gamma(5) * pbrt.Abs(new Vector3f(pr.x, pr.y, pr.z));
            return new SurfaceInteraction(new Point3fi(pr, pError), uv, wo, dpdu, dpdv, new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), time, flipNormal, 0);
        }

        public override Float Area()
        {
            return (Float)area;
        }

        private Interaction InteractionAt(double[] p, int face)
        {
            Local(p, face, out Point2f uv, out double[] pu, out double[] pv);
            Point3f pr = renderFromObject.Apply(new Point3f((Float)p[0], (Float)p[1], (Float)p[2]));
            int a = face >> 1;
            double[] n = new double[3];
            n[a] = (face & 1) == 1 ? 1 : -1;
            Normal3f nr = pbrt.Normalize(renderFromObject.Apply(new Normal3f((Float)n[0], (Float)n[1], (Float)n[2])));
            if (reverseOrientation)
            {
                nr = -nr;
            }
            Vector3f pError = pbrt.gamma(5) * pbrt.Abs(new Vector3f(pr.x, pr.y, pr.z));
            return new Interaction(new Point3fi(pr, pError), nr, uv);
        }

        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            if (!(area > 0))
            {
                return default;
            }
            double target = u.x * area;
            int face = 0;
            while (face < 5 && faceCumulative[face] <= target)
            {
                ++face;
            }
            double start = face == 0 ? 0 : faceCumulative[face - 1];
            double fx = faceArea[face] > 0 ? Math.Min((target - start) / faceArea[face], 1 - 1e-12) : 0.5;
            double fy = Math.Min((double)u.y, 1 - 1e-12);
            FaceFrame(face, out double[] origin, out double[] pu, out double[] pv);
            double[] p = new double[3];
            for (int a = 0; a < 3; ++a)
            {
                p[a] = origin[a] + fx * pu[a] + fy * pv[a];
            }
            return new ShapeSample(InteractionAt(p, face), (Float)(1 / area));
        }

        public override Float PDF(Interaction intr)
        {
            return area > 0 ? (Float)(1 / area) : 0;
        }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            return PatchSampling.ToSolidAngle(Sample(u), ctx);
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            Ray ray = ctx.SpawnRay(wi);
            if (!Slab(ray, Infinity, out double t, out int face))
            {
                return 0;
            }
            Point3f o = objectFromRender.Apply(ray.o);
            Vector3f d = objectFromRender.Apply(ray.d);
            double[] p = { o.x + t * d.x, o.y + t * d.y, o.z + t * d.z };
            Interaction at = InteractionAt(p, face);
            return PatchSampling.AreaToSolidAngle(1 / area, at.n, at.p(), ctx, wi);
        }

        public override string ToString()
        {
            return "[ VolumeBox ]";
        }
    }

    public static class Volumes
    {
        public static pstd.vector<Shape> Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation, ParameterDictionary parameters,
                                                FileLoc loc, Allocator alloc)
        {
            pstd.vector<Shape> shapes = new pstd.vector<Shape>();
            string type = parameters.GetOneString("type", "box");
            if (type != "box")
            {
                pbrt.Error(loc, "volume: only the type \"box\" is supported (\"" + type + "\" given).");
                return shapes;
            }
            double[] low = new double[3], high = new double[3];
            List<Float> bound = parameters.GetFloatArray("bound");
            if (bound.Count == 6)
            {
                for (int a = 0; a < 3; ++a)
                {
                    low[a] = bound[2 * a];
                    high[a] = bound[2 * a + 1];
                }
            }
            else
            {
                Point3f p0 = parameters.GetOnePoint3f("p0", new Point3f(float.NaN, 0, 0)), p1 = parameters.GetOnePoint3f("p1", new Point3f(float.NaN, 0, 0));
                if (float.IsNaN(p0.x) || float.IsNaN(p1.x))
                {
                    pbrt.Error(loc, "volume: \"bound\" [xmin xmax ymin ymax zmin zmax] (or \"p0\" and \"p1\") is required.");
                    return shapes;
                }
                low = new double[] { Math.Min(p0.x, p1.x), Math.Min(p0.y, p1.y), Math.Min(p0.z, p1.z) };
                high = new double[] { Math.Max(p0.x, p1.x), Math.Max(p0.y, p1.y), Math.Max(p0.z, p1.z) };
            }
            for (int a = 0; a < 3; ++a)
            {
                if (!(high[a] > low[a]))
                {
                    pbrt.Error(loc, "volume: the bound must have a positive extent in every axis.");
                    return shapes;
                }
            }
            shapes.push_back(new VolumeBox(low, high, renderFromObject, objectFromRender, reverseOrientation));
            return shapes;
        }
    }
}  // namespace pbrt
