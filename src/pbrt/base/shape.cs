// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// base/shape.h

using System;

namespace pbrt
{

    // Shape Definition (TaggedPointer<Sphere, Cylinder, Disk, Triangle, BilinearPatch, Curve>)
    public abstract unsafe partial class Shape
    {
        // Shape Interface
        public abstract override string ToString();
        public abstract Bounds3f Bounds();
        public abstract DirectionCone NormalBounds();
        public abstract pstd.optional<ShapeIntersection> Intersect(Ray ray, Float tMax = Infinity);
        public abstract bool IntersectP(Ray ray, Float tMax = Infinity);
        public abstract Float Area();
        public abstract pstd.optional<ShapeSample> Sample(Point2f u);
        public abstract Float PDF(Interaction intr);
        public abstract pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u);
        public abstract Float PDF(ShapeSampleContext ctx, Vector3f wi);

        // (not in pbrt-v4) Deformation motion blur, see docs/motion-blur.md. A shape that deforms over the shutter (its geometry has several keys)
        // intersects, samples and bounds itself as it is at the time of the ray (Intersect, IntersectP), of the context (Sample, PDF) or
        // over the whole shutter (Bounds, NormalBounds); AtTime gives the static shape that it is at a time, which is what the
        // area lights use where a function has no time. A shape that does not deform is its own AtTime.
        public virtual bool IsDeforming()
        {
            return false;
        }

        public virtual Shape AtTime(Float time)
        {
            return this;
        }

        // The area averaged over the shutter (Area() of a shape that does not deform): the power and the importance of an area light
        public virtual Float MeanArea()
        {
            return Area();
        }
    }

}  // namespace pbrt
