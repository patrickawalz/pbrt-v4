// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// cpu/primitive.h / cpu/primitive.cpp

using System;

namespace pbrt
{

    // STAT_MEMORY_COUNTER("Memory/Primitives", primitiveMemory);
    // (not tracked: managed objects have no sizeof)

    // Primitive Definition
    // (TaggedPointer over SimplePrimitive, GeometricPrimitive, TransformedPrimitive,
    // AnimatedPrimitive, BVHAggregate, KdTreeAggregate)
    public abstract unsafe partial class Primitive
    {
        // Primitive Interface
        public abstract Bounds3f Bounds();

        public abstract pstd.optional<ShapeIntersection> Intersect(Ray r, Float tMax = Infinity);
        public abstract bool IntersectP(Ray r, Float tMax = Infinity);
    }

    // GeometricPrimitive Definition
    public sealed unsafe class GeometricPrimitive : Primitive
    {
        // GeometricPrimitive Public Methods
        public GeometricPrimitive(Shape shape, Material material, Light areaLight,
                                  MediumInterface mediumInterface, FloatTexture alpha = null)
        {
            this.shape = shape;
            this.material = material;
            this.areaLight = areaLight;
            this.mediumInterface = new MediumInterface(mediumInterface.inside, mediumInterface.outside, mediumInterface.priority);
            this.alpha = alpha;
        }

        public override Bounds3f Bounds()
        {
            return shape.Bounds();
        }

        public override pstd.optional<ShapeIntersection> Intersect(Ray r, Float tMax = Infinity)
        {
            pstd.optional<ShapeIntersection> si = shape.Intersect(r, tMax);
            if (!si)
                return default;
            CHECK_LT(si.value().tHit, (Float)(1.001 * tMax));
            // Test intersection against alpha texture, if present
            if (alpha != null)
            {
                Float a = alpha.Evaluate(new TextureEvalContext(si.value().intr));
                if (a < 1)
                {
                    // Possibly ignore intersection based on stochastic alpha test
                    Float u = (a <= 0) ? 1.0f : HashFloat(r.o, r.d);
                    if (u > a)
                    {
                        // Ignore this intersection and trace a new ray
                        Ray rNext = new Ray(si.value().intr.SpawnRay(r.d));
                        pstd.optional<ShapeIntersection> siNext =
                            Intersect(rNext, tMax - si.value().tHit);
                        if (siNext)
                            siNext.Ref().tHit += si.value().tHit;
                        return siNext;
                    }
                }
            }

            // Initialize _SurfaceInteraction_ after _Shape_ intersection
            si.value().intr.SetIntersectionProperties(material, areaLight, mediumInterface);

            return si;
        }

        public override bool IntersectP(Ray r, Float tMax = Infinity)
        {
            if (alpha != null)
                return Intersect(r, tMax).has_value();
            else
                return shape.IntersectP(r, tMax);
        }

        // GeometricPrimitive Private Members
        Shape shape;
        Material material;
        Light areaLight;
        MediumInterface mediumInterface;
        FloatTexture alpha;
    }

    // SimplePrimitive Definition
    public sealed unsafe class SimplePrimitive : Primitive
    {
        // SimplePrimitive Public Methods
        public SimplePrimitive(Shape shape, Material material)
        {
            this.shape = shape;
            this.material = material;
        }

        public override Bounds3f Bounds()
        {
            return shape.Bounds();
        }

        public override bool IntersectP(Ray r, Float tMax = Infinity)
        {
            return shape.IntersectP(r, tMax);
        }

        public override pstd.optional<ShapeIntersection> Intersect(Ray r, Float tMax = Infinity)
        {
            pstd.optional<ShapeIntersection> si = shape.Intersect(r, tMax);
            if (!si)
                return default;

            si.value().intr.SetIntersectionProperties(material, null, null);

            return si;
        }

        // SimplePrimitive Private Members
        Shape shape;
        Material material;
    }

    // TransformedPrimitive Definition
    public sealed unsafe class TransformedPrimitive : Primitive
    {
        // TransformedPrimitive Public Methods
        // (Transform is a struct here, so the transform is held by value rather than by pointer.)
        public TransformedPrimitive(Primitive primitive, Transform renderFromPrimitive)
        {
            this.primitive = primitive;
            this.renderFromPrimitive = renderFromPrimitive;
        }

        public override Bounds3f Bounds() { return renderFromPrimitive.Apply(primitive.Bounds()); }

        public override pstd.optional<ShapeIntersection> Intersect(Ray r, Float tMax = Infinity)
        {
            // Transform ray to primitive-space and intersect with primitive
            Ray ray = renderFromPrimitive.ApplyInverse(r, &tMax);
            pstd.optional<ShapeIntersection> si = primitive.Intersect(ray, tMax);
            if (!si)
                return default;
            CHECK_LT(si.value().tHit, (Float)(1.001 * tMax));

            // Return transformed instance's intersection information
            si.Ref().intr = renderFromPrimitive.Apply(si.value().intr);
            CHECK_GE(Dot(si.value().intr.n, si.value().intr.shading.n), (Float)0);
            return si;
        }

        public override bool IntersectP(Ray r, Float tMax = Infinity)
        {
            Ray ray = renderFromPrimitive.ApplyInverse(r, &tMax);
            return primitive.IntersectP(ray, tMax);
        }

        // TransformedPrimitive Private Members
        Primitive primitive;
        Transform renderFromPrimitive;
    }

    // AnimatedPrimitive Definition
    public sealed unsafe class AnimatedPrimitive : Primitive
    {
        // AnimatedPrimitive Public Methods
        public override Bounds3f Bounds()
        {
            return renderFromPrimitive.MotionBounds(primitive.Bounds());
        }

        public AnimatedPrimitive(Primitive p, AnimatedTransform renderFromPrimitive)
        {
            this.primitive = p;
            this.renderFromPrimitive = renderFromPrimitive;
            CHECK(renderFromPrimitive.IsAnimated());
        }

        public override pstd.optional<ShapeIntersection> Intersect(Ray r, Float tMax = Infinity)
        {
            // Compute _ray_ after transformation by _renderFromPrimitive_
            Transform interpRenderFromPrimitive = renderFromPrimitive.Interpolate(r.time);
            Ray ray = interpRenderFromPrimitive.ApplyInverse(r, &tMax);
            pstd.optional<ShapeIntersection> si = primitive.Intersect(ray, tMax);
            if (!si)
                return default;

            // Transform instance's intersection data to rendering space
            si.Ref().intr = interpRenderFromPrimitive.Apply(si.value().intr);
            CHECK_GE(Dot(si.value().intr.n, si.value().intr.shading.n), (Float)0);
            return si;
        }

        public override bool IntersectP(Ray r, Float tMax = Infinity)
        {
            Ray ray = renderFromPrimitive.ApplyInverse(r, &tMax);
            return primitive.IntersectP(ray, tMax);
        }

        // AnimatedPrimitive Private Members
        Primitive primitive;
        AnimatedTransform renderFromPrimitive;
    }

}  // namespace pbrt
