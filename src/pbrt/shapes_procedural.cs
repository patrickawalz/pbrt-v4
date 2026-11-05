// (not in pbrt-v4) shapes_procedural.cs
//
// RiProcedural: a shape that is known by its bound and is expanded when a ray first reaches the bound. The expansion is a delegate registered by name
// (Procedurals.Register), called once with the parameters of the shape; it returns the shapes (any of the shapes of Shape.Create). A procedural that
// is never reached is never expanded. Sampling (area lights) expands the procedural and chooses a child with the probability of its share of the area.
//
//   Shape "procedural" "string name" "bound" [xmin xmax ymin ymax zmin zmax] ... (the parameters are passed to the expansion)
//
// PDF(Interaction) assumes that the children sample the area uniformly (1 / Area()).

using System;
using System.Collections.Generic;

namespace pbrt
{
    public delegate pstd.vector<Shape> ProceduralExpansion(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation,
                                                          ParameterDictionary parameters, FileLoc loc, Allocator alloc);

    public static class Procedurals
    {
        private static readonly Dictionary<string, ProceduralExpansion> registry = new Dictionary<string, ProceduralExpansion>();

        public static void Register(string name, ProceduralExpansion expansion)
        {
            lock (registry)
            {
                registry[name] = expansion;
            }
        }

        public static ProceduralExpansion Find(string name)
        {
            lock (registry)
            {
                ProceduralExpansion result;
                return registry.TryGetValue(name, out result) ? result : null;
            }
        }

        public static pstd.vector<Shape> Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation, ParameterDictionary parameters,
                                                FileLoc loc, Allocator alloc)
        {
            pstd.vector<Shape> shapes = new pstd.vector<Shape>();
            string name = parameters.GetOneString("name", "");
            ProceduralExpansion expansion = Find(name);
            if (expansion == null)
            {
                pbrt.Error(loc, "procedural: no procedural named \"" + name + "\" is registered.");
                return shapes;
            }
            List<Float> bound = parameters.GetFloatArray("bound");
            if (bound.Count != 6 || !(bound[0] <= bound[1]) || !(bound[2] <= bound[3]) || !(bound[4] <= bound[5]))
            {
                pbrt.Error(loc, "procedural: \"bound\" must be [xmin xmax ymin ymax zmin zmax].");
                return shapes;
            }
            Bounds3f result = new Bounds3f();
            for (int corner = 0; corner < 8; ++corner)
            {
                Point3f p = renderFromObject.Apply(new Point3f(bound[(corner & 1) != 0 ? 1 : 0], bound[2 + ((corner & 2) != 0 ? 1 : 0)], bound[4 + ((corner & 4) != 0 ? 1 : 0)]));
                result = corner == 0 ? new Bounds3f(p) : pbrt.Union(result, p);
            }
            shapes.push_back(new ProceduralShape(result, () => expansion(renderFromObject, objectFromRender, reverseOrientation, parameters, loc, alloc)));
            return shapes;
        }
    }

    public sealed unsafe class ProceduralShape : Shape
    {
        private readonly Bounds3f bounds;
        private readonly Func<pstd.vector<Shape>> expand;
        private readonly object sync = new object();
        private volatile Shape[] children;
        private double[] cumulativeArea;
        private double totalArea;
        private int expansions;

        public ProceduralShape(Bounds3f bounds, Func<pstd.vector<Shape>> expand)
        {
            this.bounds = bounds;
            this.expand = expand;
        }

        // The number of times that the expansion ran (1 once the bound was reached)
        public int Expansions
        {
            get { return expansions; }
        }

        private Shape[] Children()
        {
            Shape[] result = children;
            if (result != null)
            {
                return result;
            }
            lock (sync)
            {
                if (children == null)
                {
                    pstd.vector<Shape> made = expand();
                    Shape[] array = new Shape[made.size()];
                    double[] cumulative = new double[array.Length];
                    double running = 0;
                    for (int i = 0; i < array.Length; ++i)
                    {
                        array[i] = made[i];
                        running += array[i].Area();
                        cumulative[i] = running;
                    }
                    cumulativeArea = cumulative;
                    totalArea = running;
                    ++expansions;
                    children = array;
                }
                return children;
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

        private bool Reaches(Ray ray, Float tMax)
        {
            return bounds.IntersectP(ray.o, ray.d, tMax);
        }

        public override pstd.optional<ShapeIntersection> Intersect(Ray ray, Float tMax = Infinity)
        {
            if (!Reaches(ray, tMax))
            {
                return default;
            }
            pstd.optional<ShapeIntersection> best = default;
            foreach (Shape child in Children())
            {
                pstd.optional<ShapeIntersection> si = child.Intersect(ray, best.has_value() ? best.value().tHit : tMax);
                if (si.has_value())
                {
                    best = si;
                }
            }
            return best;
        }

        public override bool IntersectP(Ray ray, Float tMax = Infinity)
        {
            if (!Reaches(ray, tMax))
            {
                return false;
            }
            foreach (Shape child in Children())
            {
                if (child.IntersectP(ray, tMax))
                {
                    return true;
                }
            }
            return false;
        }

        public override Float Area()
        {
            Children();
            return (Float)totalArea;
        }

        private int Choose(ref Float u)
        {
            Shape[] c = Children();
            double target = u * totalArea;
            int low = 0, high = c.Length - 1;
            while (low < high)
            {
                int mid = (low + high) >> 1;
                if (cumulativeArea[mid] > target)
                {
                    high = mid;
                }
                else
                {
                    low = mid + 1;
                }
            }
            double start = low == 0 ? 0 : cumulativeArea[low - 1];
            double share = cumulativeArea[low] - start;
            u = share > 0 ? (Float)Math.Min((target - start) / share, 1 - 1e-7) : 0.5f;
            return low;
        }

        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            Shape[] c = Children();
            if (c.Length == 0 || !(totalArea > 0))
            {
                return default;
            }
            Float x = u.x;
            int i = Choose(ref x);
            pstd.optional<ShapeSample> ss = c[i].Sample(new Point2f(x, u.y));
            if (ss.has_value())
            {
                ss.Ref().pdf *= (Float)(c[i].Area() / totalArea);
            }
            return ss;
        }

        public override Float PDF(Interaction intr)
        {
            Children();
            return totalArea > 0 ? (Float)(1 / totalArea) : 0;
        }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            Shape[] c = Children();
            if (c.Length == 0 || !(totalArea > 0))
            {
                return default;
            }
            Float x = u.x;
            int i = Choose(ref x);
            pstd.optional<ShapeSample> ss = c[i].Sample(ctx, new Point2f(x, u.y));
            if (ss.has_value())
            {
                ss.Ref().pdf *= (Float)(c[i].Area() / totalArea);
            }
            return ss;
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            // the density of the direction is the sum over the children of the probability of the child and its density
            Shape[] c = Children();
            double sum = 0;
            for (int i = 0; i < c.Length; ++i)
            {
                sum += c[i].Area() / totalArea * c[i].PDF(ctx, wi);
            }
            return (Float)sum;
        }

        public override string ToString()
        {
            return "[ ProceduralShape ]";
        }
    }
}  // namespace pbrt
