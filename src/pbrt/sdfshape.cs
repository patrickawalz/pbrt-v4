// (not in pbrt-v4) sdfshape.cs
//
// Shape "sdf": a surface defined by a signed distance field (see sdf.cs), the counterpart of OctaneRender's Vectron. Rays are
// intersected by sphere tracing inside a user supplied box, in double precision and in object space, with the over-relaxation of
// Keinert et al. (Enhanced Sphere Tracing, 2014) and a Newton polish of the hit. The surface is the zero set; the field is
// negative inside, so the shape is a closed volume boundary for the boundary stack (mediumpriority, material volumes) and an
// area light shape (uniform sampling by area, see sdfsampling.cs).
//
//   Shape "sdf" "string sdf" "smoothunion(sphere(1), translate(box(.5,.5,.5), 1,0,0), .3)"
//         "point3 pmin" [-2 -2 -2] "point3 pmax" [3 2 2]
//
// Optional: "float epsilon" (the hit tolerance relative to the box, 1e-8), "integer maxsteps" (32768), "float relaxation" (1.2, 1
// is plain sphere tracing), "float lipschitz" (a factor on the Lipschitz bound that shortens the steps, 1), "string uvmapping"
// ("spherical", "cylindrical", "planar", "box"), "integer tessellation" (the cells along the longest side of the box used to find
// the surface for light sampling, 64) and "integer areasubdivisions" (3).

using System;

namespace pbrt
{

    // SdfUvMapping Definition
    public enum SdfUvMapping
    {
        Spherical,
        Cylindrical,
        Planar,
        Box,
        PlanarYZ,
        PlanarXZ
    }

    // SdfShapeOptions Definition
    public sealed class SdfShapeOptions
    {
        public double epsilon = 1e-8;
        public int maxSteps = 32768;
        public double relaxation = 1.2;
        public double validation = 2;
        public double lipschitzFactor = 1;
        public SdfUvMapping uvMapping = SdfUvMapping.Spherical;
        public int tessellation = 64;
        public int areaSubdivisions = 3;
        // Whether an area light picks its sample points in proportion to the importance of the triangles for the receiving point
        // (otherwise uniformly by area)
        public bool importanceSampling = true;
    }

    // SdfShape Definition
    public sealed unsafe class SdfShape : Shape
    {
        // SdfShape Public Methods
        public SdfShape(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation, SdfNode field,
                        Point3f lower, Point3f upper, SdfShapeOptions options)
        {
            this.renderFromObject = renderFromObject;
            this.objectFromRender = objectFromRender;
            this.reverseOrientation = reverseOrientation;
            transformSwapsHandedness = renderFromObject.SwapsHandedness();
            this.field = field;
            this.options = options;
            this.lower = new SdfVector(lower.x, lower.y, lower.z);
            this.upper = new SdfVector(upper.x, upper.y, upper.z);
            lipschitz = field.Lipschitz() * options.lipschitzFactor;
            localBounds = field.HasLocalBounds();
            rigorousField = field.IsRigorous();
            SdfVector extent = this.upper - this.lower;
            scale = extent.Length();
            epsilon = options.epsilon * scale;
            gradientStep = 1e-6 * scale;
            objectFromRenderMatrix = Matrix3x4(objectFromRender);
            renderFromObjectMatrix = Matrix3x4(renderFromObject);
        }

        private static double[] Matrix3x4(Transform t)
        {
            SquareMatrix4 m = t.GetMatrix();
            double[] r = new double[12];
            for (int i = 0; i < 3; ++i)
            {
                for (int j = 0; j < 4; ++j)
                {
                    r[4 * i + j] = m[i][j];
                }
            }
            return r;
        }

        public SdfNode Field()
        {
            return field;
        }

        public double LipschitzBound()
        {
            return lipschitz;
        }

        public override Bounds3f Bounds()
        {
            return renderFromObject.Apply(new Bounds3f(new Point3f((Float)lower.x, (Float)lower.y, (Float)lower.z),
                                                       new Point3f((Float)upper.x, (Float)upper.y, (Float)upper.z)));
        }

        public override DirectionCone NormalBounds()
        {
            return DirectionCone.EntireSphere();
        }

        // Sphere tracing

        // Traces a ray given in object space with a unit direction between the distances u0 and u1; returns the distance of the hit.
        // Over-relaxed sphere tracing (Keinert et al. 2014): a step is omega times the radius of the safe sphere, and when the new
        // sphere does not overlap the previous one the step is taken back to the unrelaxed one. The step is signed, so a step that
        // overshoots the surface is followed by one that comes back, from either side of the surface.
        private bool March(SdfVector o, SdfVector w, double u0, double u1, bool polish, out double uHit)
        {
            uHit = 0;
            double u = u0;
            double previousRadius = 0, stepLength = 0, omega = rigorousField ? options.relaxation : 1.0, previousU = u0;
            double side = field.Evaluate(o + w * u0) < 0 ? -1 : 1;
            bool validate = !rigorousField && options.validation > 0;
            double previousSigned = 0;
            for (int step = 0; step < options.maxSteps; ++step)
            {
                SdfVector p = o + w * u;
                double f = field.Evaluate(p);
                double distance = Math.Abs(f);
                if (validate && step > 0 && Math.Abs(stepLength) > 8 * epsilon &&
                    previousSigned - side * f > options.validation * Math.Abs(stepLength))
                {
                    // The estimate fell faster than a distance can along this step, so it overestimated the distance at the previous
                    // point: take half of the step instead (the previous point stays the reference)
                    stepLength *= 0.5;
                    u = previousU + stepLength;
                    omega = 1;
                    continue;
                }
                previousSigned = side * f;
                double signedRadius = side * f / StepBound(p, distance);
                double radius = Math.Abs(signedRadius);
                if (omega > 1 && radius + previousRadius < stepLength)
                {
                    // The spheres do not overlap: go back to the safe step of the previous sphere and trace without relaxation
                    u = previousU + previousRadius;
                    stepLength = previousRadius;
                    omega = 1;
                    continue;
                }
                if (distance <= epsilon)
                {
                    uHit = u;
                    if (polish)
                    {
                        uHit = Polish(o, w, u, f);
                    }
                    return uHit <= u1 * (1 + 1e-12);
                }
                previousU = u;
                previousRadius = radius;
                stepLength = omega * signedRadius;
                u += stepLength;
                if (u > u1)
                {
                    if (omega > 1 && stepLength > previousRadius)
                    {
                        // The relaxed step left the box; the unrelaxed one may still reach a surface inside it
                        u = previousU + previousRadius;
                        stepLength = previousRadius;
                        omega = 1;
                        if (u > u1)
                        {
                            return false;
                        }
                        continue;
                    }
                    return false;
                }
            }
            return false;
        }

        // The Lipschitz bound to divide the distance by at p: the global one, or for a field with local bounds the bound over a ball
        // that the step stays in (segment tracing). A step of radius r is safe when the bound over the ball of radius r is at most
        // distance / r. The bound over the ball of radius distance is one such bound (the step distance / bound is inside that ball);
        // a second try with the bound over the smaller ball is used when the larger step it allows passes its own test.
        private double StepBound(SdfVector p, double distance)
        {
            if (!localBounds || distance == 0)
            {
                return lipschitz;
            }
            double bound = Math.Min(field.Lipschitz(p, distance) * options.lipschitzFactor, lipschitz);
            if (bound < 1)
            {
                // A step of distance / bound would leave the ball that the bound was found for
                return lipschitz;
            }
            double smaller = Math.Min(field.Lipschitz(p, distance / bound) * options.lipschitzFactor, bound);
            if (smaller < bound && smaller >= 1)
            {
                double larger = Math.Min(field.Lipschitz(p, distance / smaller) * options.lipschitzFactor, bound);
                if (larger <= smaller * (1 + 1e-12))
                {
                    return smaller;
                }
            }
            return bound;
        }

        // Two Newton steps along the ray from a point that is already within the tolerance of the surface
        private double Polish(SdfVector o, SdfVector w, double u, double f)
        {
            for (int i = 0; i < 2; ++i)
            {
                SdfVector p = o + w * u;
                SdfVector g = field.Gradient(p, gradientStep);
                double slope = SdfVector.Dot(g, w);
                if (Math.Abs(slope) < 0.05 * g.Length() || g.Length() == 0)
                {
                    break;
                }
                double next = u - f / slope;
                if (Math.Abs(next - u) > 100 * epsilon)
                {
                    break;
                }
                u = next;
                f = field.Evaluate(o + w * u);
            }
            return u;
        }

        // The ray in object space: origin, unit direction, the length of the transformed direction, and the box interval in distance
        private bool PrepareRay(Ray ray, Float tMax, out SdfVector o, out SdfVector w, out double dirLength, out double u0,
                                out double u1)
        {
            double[] m = objectFromRenderMatrix;
            double ox = ray.o.x, oy = ray.o.y, oz = ray.o.z, dx = ray.d.x, dy = ray.d.y, dz = ray.d.z;
            o = new SdfVector(m[0] * ox + m[1] * oy + m[2] * oz + m[3], m[4] * ox + m[5] * oy + m[6] * oz + m[7],
                              m[8] * ox + m[9] * oy + m[10] * oz + m[11]);
            SdfVector d = new SdfVector(m[0] * dx + m[1] * dy + m[2] * dz, m[4] * dx + m[5] * dy + m[6] * dz,
                                        m[8] * dx + m[9] * dy + m[10] * dz);
            dirLength = d.Length();
            w = d / dirLength;
            u0 = 0;
            u1 = double.IsInfinity(tMax) ? double.MaxValue : tMax * dirLength;
            for (int axis = 0; axis < 3; ++axis)
            {
                double near, far;
                if (w[axis] == 0)
                {
                    if (o[axis] < lower[axis] || o[axis] > upper[axis])
                    {
                        return false;
                    }
                    continue;
                }
                near = (lower[axis] - o[axis]) / w[axis];
                far = (upper[axis] - o[axis]) / w[axis];
                if (near > far)
                {
                    double swap = near;
                    near = far;
                    far = swap;
                }
                u0 = Math.Max(u0, near);
                u1 = Math.Min(u1, far * (1 + 1e-12));
                if (u0 > u1)
                {
                    return false;
                }
            }
            return true;
        }

        public override bool IntersectP(Ray ray, Float tMax = Infinity)
        {
            SdfTime.Current = ray.time;
            if (!PrepareRay(ray, tMax, out SdfVector o, out SdfVector w, out double dirLength, out double u0, out double u1))
            {
                return false;
            }
            if (!March(o, w, u0, u1, false, out double u))
            {
                return false;
            }
            return u / dirLength > 0 && u / dirLength <= tMax;
        }

        public override pstd.optional<ShapeIntersection> Intersect(Ray ray, Float tMax = Infinity)
        {
            SdfTime.Current = ray.time;
            if (!PrepareRay(ray, tMax, out SdfVector o, out SdfVector w, out double dirLength, out double u0, out double u1))
            {
                return default;
            }
            if (!March(o, w, u0, u1, true, out double u))
            {
                return default;
            }
            double tHit = u / dirLength;
            if (!(tHit > 0) || tHit > tMax)
            {
                return default;
            }
            SdfVector pObject = o + w * u;
            SurfaceInteraction intr = InteractionFromPoint(pObject, objectFromRender.Apply(-ray.d), ray.time);
            return new ShapeIntersection(intr, (Float)tHit);
        }

        // The unit outward normal (the normalized gradient) at an object space point
        private SdfVector NormalAt(SdfVector p)
        {
            return field.Gradient(p, gradientStep).Normalized();
        }

        // The mapping from the position to (u, v), with the gradients of u and v with respect to the position
        private void UvMapping(SdfVector p, SdfVector n, out Point2f uv, out SdfVector gradU, out SdfVector gradV)
        {
            double twoPi = 2 * Math.PI;
            double rho2 = p.x * p.x + p.y * p.y;
            double rho = Math.Sqrt(rho2);
            double rho2Safe = Math.Max(rho2, 1e-30);
            double phi = Math.Atan2(p.y, p.x);
            if (phi < 0)
            {
                phi += twoPi;
            }
            SdfVector gradPhi = new SdfVector(-p.y / rho2Safe, p.x / rho2Safe, 0);
            SdfUvMapping mode = options.uvMapping;
            if (mode == SdfUvMapping.Box)
            {
                int axis = Math.Abs(n.x) >= Math.Abs(n.y) && Math.Abs(n.x) >= Math.Abs(n.z) ? 0 : (Math.Abs(n.y) >= Math.Abs(n.z) ? 1 : 2);
                if (axis == 0)
                {
                    uv = new Point2f((Float)p.y, (Float)p.z);
                    gradU = new SdfVector(0, 1, 0);
                    gradV = new SdfVector(0, 0, 1);
                }
                else if (axis == 1)
                {
                    uv = new Point2f((Float)p.x, (Float)p.z);
                    gradU = new SdfVector(1, 0, 0);
                    gradV = new SdfVector(0, 0, 1);
                }
                else
                {
                    uv = new Point2f((Float)p.x, (Float)p.y);
                    gradU = new SdfVector(1, 0, 0);
                    gradV = new SdfVector(0, 1, 0);
                }
                return;
            }
            if (mode == SdfUvMapping.Planar)
            {
                uv = new Point2f((Float)p.x, (Float)p.y);
                gradU = new SdfVector(1, 0, 0);
                gradV = new SdfVector(0, 1, 0);
                return;
            }
            if (mode == SdfUvMapping.PlanarYZ)
            {
                uv = new Point2f((Float)p.y, (Float)p.z);
                gradU = new SdfVector(0, 1, 0);
                gradV = new SdfVector(0, 0, 1);
                return;
            }
            if (mode == SdfUvMapping.PlanarXZ)
            {
                uv = new Point2f((Float)p.x, (Float)p.z);
                gradU = new SdfVector(1, 0, 0);
                gradV = new SdfVector(0, 0, 1);
                return;
            }
            if (mode == SdfUvMapping.Cylindrical)
            {
                uv = new Point2f((Float)(phi / twoPi), (Float)p.z);
                gradU = gradPhi / twoPi;
                gradV = new SdfVector(0, 0, 1);
                return;
            }
            double r = p.Length();
            double r2 = Math.Max(r * r, 1e-30);
            double theta = Math.Acos(Math.Min(Math.Max(p.z / Math.Max(r, 1e-30), -1), 1));
            uv = new Point2f((Float)(phi / twoPi), (Float)(theta / Math.PI));
            gradU = gradPhi / twoPi;
            double rhoSafe = Math.Max(rho, 1e-15);
            SdfVector gradTheta = new SdfVector(p.x * p.z / (r2 * rhoSafe), p.y * p.z / (r2 * rhoSafe), -rho / r2);
            gradV = gradTheta / Math.PI;
        }

        private SurfaceInteraction InteractionFromPoint(SdfVector pObject, Vector3f woObject, Float time)
        {
            SdfVector n = NormalAt(pObject);
            UvMapping(pObject, n, out Point2f uv, out SdfVector gradU, out SdfVector gradV);

            // The tangent vectors dual to the gradients of u and v, restricted to the tangent plane
            SdfVector tangentU = gradU - n * SdfVector.Dot(gradU, n);
            SdfVector tangentV = gradV - n * SdfVector.Dot(gradV, n);
            double g00 = SdfVector.Dot(tangentU, tangentU), g01 = SdfVector.Dot(tangentU, tangentV),
                   g11 = SdfVector.Dot(tangentV, tangentV);
            double determinant = g00 * g11 - g01 * g01;
            SdfVector dpdu, dpdv;
            if (determinant > 1e-24 * g00 * g11 && determinant > 0)
            {
                dpdu = tangentU * (g11 / determinant) - tangentV * (g01 / determinant);
                dpdv = tangentV * (g00 / determinant) - tangentU * (g01 / determinant);
            }
            else
            {
                Vector3f nf = new Vector3f((Float)n.x, (Float)n.y, (Float)n.z);
                Vector3f t1, t2;
                pbrt.CoordinateSystem(nf, &t1, &t2);
                dpdu = new SdfVector(t1.x, t1.y, t1.z);
                dpdv = new SdfVector(t2.x, t2.y, t2.z);
            }

            // The change of the normal along the tangent vectors
            SdfVector dndu = NormalDerivative(pObject, dpdu), dndv = NormalDerivative(pObject, dpdv);

            // The residual of the field bounds the distance of the point to the true surface
            double residual = Math.Abs(field.Evaluate(pObject)) / Math.Max(lipschitz, 1e-300);
            Vector3f pError = pbrt.gamma(5) * pbrt.Abs(new Vector3f((Float)pObject.x, (Float)pObject.y, (Float)pObject.z)) +
                              new Vector3f((Float)residual, (Float)residual, (Float)residual);

            bool flipNormal = reverseOrientation ^ transformSwapsHandedness;
            SurfaceInteraction si = new SurfaceInteraction(
                new Point3fi(new Point3f((Float)pObject.x, (Float)pObject.y, (Float)pObject.z), pError), uv, woObject,
                ToVector(dpdu), ToVector(dpdv), new Normal3f((Float)dndu.x, (Float)dndu.y, (Float)dndu.z),
                new Normal3f((Float)dndv.x, (Float)dndv.y, (Float)dndv.z), time, flipNormal);
            // The geometric normal is the outward gradient, not the cross product of the tangents (their order depends on the mapping)
            Normal3f normal = new Normal3f((Float)n.x, (Float)n.y, (Float)n.z);
            if (flipNormal)
            {
                normal *= -1;
            }
            si.n = normal;
            si.shading.n = normal;
            return renderFromObject.Apply(si);
        }

        private static Vector3f ToVector(SdfVector v)
        {
            return new Vector3f((Float)v.x, (Float)v.y, (Float)v.z);
        }

        // The derivative of the unit normal along a tangent vector, by a central difference of normals a fixed distance away
        private SdfVector NormalDerivative(SdfVector p, SdfVector tangent)
        {
            double length = tangent.Length();
            if (length == 0)
            {
                return new SdfVector(0, 0, 0);
            }
            double distance = 1e-3 * scale;
            SdfVector move = tangent * (distance / length);
            SdfVector difference = NormalAt(p + move) - NormalAt(p - move);
            return difference * (length / (2 * distance));
        }

        // Sampling by area (see SdfSurfaceSampler)

        private SdfSurfaceSampler Sampler()
        {
            if (sampler == null)
            {
                lock (samplerLock)
                {
                    if (sampler == null)
                    {
                        sampler = new SdfSurfaceSampler(field, lipschitz, lower, upper, renderFromObjectMatrix,
                                                        options.tessellation, options.areaSubdivisions, gradientStep);
                    }
                }
            }
            return sampler;
        }

        public override Float Area()
        {
            return (Float)Sampler().Area();
        }

        // The interaction at a point of the surface (object space)
        private Interaction SurfacePoint(SdfVector pObject)
        {
            SdfVector n = NormalAt(pObject);
            Normal3f nObject = new Normal3f((Float)n.x, (Float)n.y, (Float)n.z);
            Normal3f normal = pbrt.Normalize(renderFromObject.Apply(nObject));
            if (reverseOrientation ^ transformSwapsHandedness)
            {
                normal *= -1;
            }
            UvMapping(pObject, n, out Point2f uv, out SdfVector gradU, out SdfVector gradV);
            double residual = Math.Abs(field.Evaluate(pObject)) / Math.Max(lipschitz, 1e-300);
            Vector3f pError = pbrt.gamma(5) * pbrt.Abs(new Vector3f((Float)pObject.x, (Float)pObject.y, (Float)pObject.z)) +
                              new Vector3f((Float)residual, (Float)residual, (Float)residual);
            Point3fi pi = renderFromObject.Apply(
                new Point3fi(new Point3f((Float)pObject.x, (Float)pObject.y, (Float)pObject.z), pError));
            return new Interaction(pi, normal, uv);
        }

        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            SdfSurfaceSampler surface = Sampler();
            SdfVector pObject = surface.Sample(u[0], u[1]);
            return new ShapeSample(SurfacePoint(pObject), 1 / (Float)surface.Area());
        }

        public override Float PDF(Interaction intr)
        {
            return 1 / Area();
        }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            // Sample by area (uniformly, or by the importance of the triangles for the receiving point) and convert the density to
            // solid angle
            pstd.optional<ShapeSample> ss;
            if (options.importanceSampling)
            {
                SdfSurfaceSampler surface = Sampler();
                SdfVector pObject = surface.SampleFrom(new SdfVector(ctx.p().x, ctx.p().y, ctx.p().z), u[0], u[1], out double areaDensity);
                if (areaDensity <= 0)
                {
                    return default;
                }
                ss = new ShapeSample(SurfacePoint(pObject), (Float)areaDensity);
            }
            else
            {
                ss = Sample(u);
            }
            if (!ss)
            {
                return default;
            }
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
            // Intersect the sample ray with the surface and convert the density per area of the first hit to solid angle
            Ray ray = ctx.SpawnRay(wi);
            pstd.optional<ShapeIntersection> isect = Intersect(ray);
            if (!isect)
            {
                return 0;
            }
            Float areaDensity = 1 / Area();
            if (options.importanceSampling)
            {
                Point3f receiver = ctx.p(), hit = isect.value().intr.p();
                areaDensity = (Float)Sampler().DensityFrom(new SdfVector(receiver.x, receiver.y, receiver.z),
                                                           new SdfVector(hit.x, hit.y, hit.z));
            }
            Float pdf = areaDensity / (pbrt.AbsDot(isect.value().intr.n, -wi) /
                                        pbrt.DistanceSquared(ctx.p(), isect.value().intr.p()));
            if (pbrt.IsInf(pdf))
            {
                pdf = 0;
            }
            return pdf;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ SdfShape renderFromObject: %s reverseOrientation: %s lipschitz: %f epsilon: %f ]",
                                     renderFromObject, reverseOrientation, (Float)lipschitz, (Float)epsilon);
        }

        // Creates the shape from its parameters
        public static SdfShape Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation,
                                      ParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            string expression = parameters.GetOneString("sdf", "");
            if (expression.Length == 0)
            {
                pbrt.ErrorExit(loc, "sdf: the \"string sdf\" expression is required.");
            }
            Point3f lower = parameters.GetOnePoint3f("pmin", new Point3f(-1.5f, -1.5f, -1.5f));
            Point3f upper = parameters.GetOnePoint3f("pmax", new Point3f(1.5f, 1.5f, 1.5f));
            if (lower.x >= upper.x || lower.y >= upper.y || lower.z >= upper.z)
            {
                pbrt.ErrorExit(loc, "sdf: \"pmin\" must be below \"pmax\" in every coordinate.");
            }
            SdfShapeOptions options = new SdfShapeOptions();
            options.epsilon = parameters.GetOneFloat("epsilon", (Float)options.epsilon);
            options.maxSteps = parameters.GetOneInt("maxsteps", options.maxSteps);
            options.relaxation = Math.Max(1.0, parameters.GetOneFloat("relaxation", (Float)options.relaxation));
            options.validation = Math.Max(0.0, parameters.GetOneFloat("validation", (Float)options.validation));
            options.lipschitzFactor = parameters.GetOneFloat("lipschitz", 1);
            options.tessellation = parameters.GetOneInt("tessellation", options.tessellation);
            options.areaSubdivisions = parameters.GetOneInt("areasubdivisions", options.areaSubdivisions);
            options.importanceSampling = parameters.GetOneBool("importance", options.importanceSampling);
            string mapping = parameters.GetOneString("uvmapping", "spherical");
            if (mapping == "spherical")
            {
                options.uvMapping = SdfUvMapping.Spherical;
            }
            else if (mapping == "cylindrical")
            {
                options.uvMapping = SdfUvMapping.Cylindrical;
            }
            else if (mapping == "planar")
            {
                options.uvMapping = SdfUvMapping.Planar;
            }
            else if (mapping == "box" || mapping == "triplanar")
            {
                // The face is chosen by the dominant axis of the normal (a blend of three lookups is the "triplanar" texture)
                options.uvMapping = SdfUvMapping.Box;
            }
            else if (mapping == "planaryz" || mapping == "yz")
            {
                options.uvMapping = SdfUvMapping.PlanarYZ;
            }
            else if (mapping == "planarxz" || mapping == "xz")
            {
                options.uvMapping = SdfUvMapping.PlanarXZ;
            }
            else
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: unknown sdf \"uvmapping\".", mapping));
            }
            SdfParseContext context = new SdfParseContext();
            context.domainRadius = Math.Sqrt(Math.Max(lower.x * lower.x, upper.x * upper.x) +
                                             Math.Max(lower.y * lower.y, upper.y * upper.y) +
                                             Math.Max(lower.z * lower.z, upper.z * upper.z));
            context.meshLoader = SdfMeshLoader.Load;
            context.vdbLoader = SdfVdbLoader.Load;
            SdfNode field = SdfParser.Parse(expression, context);
            if (parameters.GetOneBool("compile", true))
            {
                field = SdfCompiledNode.Compile(field);
            }
            return new SdfShape(renderFromObject, objectFromRender, reverseOrientation, field, lower, upper, options);
        }

        // SdfShape Private Members
        private readonly Transform renderFromObject, objectFromRender;
        private readonly bool reverseOrientation, transformSwapsHandedness;
        private readonly SdfNode field;
        private readonly SdfShapeOptions options;
        private readonly SdfVector lower, upper;
        private readonly double lipschitz, scale, epsilon, gradientStep;
        private readonly bool localBounds, rigorousField;
        private readonly double[] objectFromRenderMatrix, renderFromObjectMatrix;
        private SdfSurfaceSampler sampler;
        private readonly object samplerLock = new object();
    }

}  // namespace pbrt
