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
    }

}  // namespace pbrt
