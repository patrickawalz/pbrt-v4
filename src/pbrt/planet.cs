// (not in pbrt-v4) A planet: a full sphere that is intersected in double precision. The single precision quadrics of pbrt have an
// error of about half a metre at the radius of the Earth (6.36e6 m), which is too much for a camera on the ground.
//
//   Shape "planet" "float radius" 6360000 "point3 center" [0 -6360000 0]
//
// The pole axis is +y. The shape cannot be an area light and it ignores the rest of the transformation except for the centre.

using System;

namespace pbrt
{

    // PlanetShape Definition
    public sealed unsafe class PlanetShape : Shape
    {
        // PlanetShape Public Methods
        public PlanetShape(double centerX, double centerY, double centerZ, double radius, bool reverseOrientation)
        {
            cx = centerX;
            cy = centerY;
            cz = centerZ;
            this.radius = radius;
            this.reverseOrientation = reverseOrientation;
        }

        public override string ToString()
        {
            return StringPrintf("[ PlanetShape radius: %f ]", (Float)radius);
        }

        public override Bounds3f Bounds()
        {
            return new Bounds3f(new Point3f((Float)(cx - radius), (Float)(cy - radius), (Float)(cz - radius)),
                                new Point3f((Float)(cx + radius), (Float)(cy + radius), (Float)(cz + radius)));
        }

        public override DirectionCone NormalBounds() { return DirectionCone.EntireSphere(); }

        // The nearest ray parameter t in (0, tMax] of the sphere, in double precision; false if there is none
        bool Solve(Ray r, Float tMax, out double tHit)
        {
            tHit = 0;
            double ox = r.o.x - cx, oy = r.o.y - cy, oz = r.o.z - cz;
            double dx = r.d.x, dy = r.d.y, dz = r.d.z;
            double a = dx * dx + dy * dy + dz * dz;
            double b = 2 * (ox * dx + oy * dy + oz * dz);
            double distance = Math.Sqrt(ox * ox + oy * oy + oz * oz);
            double c = (distance - radius) * (distance + radius);

            // The discriminant from the distance of the line to the centre, which does not cancel for rays that start near the surface
            double f = b / (2 * a);
            double vx = ox - f * dx, vy = oy - f * dy, vz = oz - f * dz;
            double length = Math.Sqrt(vx * vx + vy * vy + vz * vz);
            double discrim = 4 * a * (radius + length) * (radius - length);
            if (discrim < 0)
            {
                return false;
            }
            double rootDiscrim = Math.Sqrt(discrim);
            double q = b < 0 ? -0.5 * (b - rootDiscrim) : -0.5 * (b + rootDiscrim);
            double t0 = q / a;
            double t1 = c / q;
            if (t0 > t1)
            {
                double tmp = t0;
                t0 = t1;
                t1 = tmp;
            }
            if (t0 > tMax || t1 <= 0)
            {
                return false;
            }
            tHit = t0;
            if (tHit <= 0)
            {
                tHit = t1;
                if (tHit > tMax)
                {
                    return false;
                }
            }
            return true;
        }

        public override pstd.optional<ShapeIntersection> Intersect(Ray ray, Float tMax = Infinity)
        {
            double t;
            if (!Solve(ray, tMax, out t))
            {
                return default;
            }

            // The hit point relative to the centre, put exactly on the sphere
            double x = ray.o.x + t * ray.d.x - cx, y = ray.o.y + t * ray.d.y - cy, z = ray.o.z + t * ray.d.z - cz;
            double scale = radius / Math.Sqrt(x * x + y * y + z * z);
            x *= scale;
            y *= scale;
            z *= scale;
            if (x == 0 && z == 0)
            {
                x = 1e-5 * radius;
            }
            double zRadius = Math.Sqrt(x * x + z * z);
            double cosPhi = x / zRadius, sinPhi = z / zRadius;
            double phi = Math.Atan2(z, x);
            if (phi < 0)
            {
                phi += 2 * Math.PI;
            }
            double theta = Math.Acos(Math.Clamp(y / radius, -1.0, 1.0));

            Vector3f dpdu = new Vector3f((Float)(-2 * Math.PI * z), 0, (Float)(2 * Math.PI * x));
            Vector3f dpdv = new Vector3f((Float)(Math.PI * y * cosPhi), (Float)(-Math.PI * zRadius), (Float)(Math.PI * y * sinPhi));
            Normal3f dndu = new Normal3f((Float)(dpdu.x / radius), (Float)(dpdu.y / radius), (Float)(dpdu.z / radius));
            Normal3f dndv = new Normal3f((Float)(dpdv.x / radius), (Float)(dpdv.y / radius), (Float)(dpdv.z / radius));

            Point3f pHit = new Point3f((Float)(x + cx), (Float)(y + cy), (Float)(z + cz));
            Vector3f pError = gamma(5) * Abs((Vector3f)pHit);
            Point2f uv = new Point2f((Float)(phi / (2 * Math.PI)), (Float)(theta / Math.PI));
            SurfaceInteraction intr = new SurfaceInteraction(new Point3fi(pHit, pError), uv, -ray.d, dpdu, dpdv, dndu, dndv, ray.time,
                                                             reverseOrientation);
            return new ShapeIntersection(intr, (Float)t);
        }

        public override bool IntersectP(Ray ray, Float tMax = Infinity)
        {
            double t;
            return Solve(ray, tMax, out t);
        }

        public override Float Area() { return (Float)(4 * Math.PI * radius * radius); }

        public override pstd.optional<ShapeSample> Sample(Point2f u) { return default; }

        public override Float PDF(Interaction intr) { return 0; }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u) { return default; }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi) { return 0; }

        public static PlanetShape Create(Transform renderFromObject, bool reverseOrientation, ParameterDictionary parameters, FileLoc loc)
        {
            Float radius = parameters.GetOneFloat("radius", 6360000f);
            Point3f center = parameters.GetOnePoint3f("center", new Point3f(0f, -radius, 0f));
            center = renderFromObject.Apply(center);
            return new PlanetShape(center.x, center.y, center.z, radius, reverseOrientation);
        }

        // PlanetShape Private Members
        readonly double cx, cy, cz, radius;
        readonly bool reverseOrientation;
    }

}  // namespace pbrt
