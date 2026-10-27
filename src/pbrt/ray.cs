// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// ray.h, ray.cpp
//
// C++ RayDifferential inherits from Ray; C# structs cannot inherit, so RayDifferential
// repeats Ray's members and converts to/from Ray (C++ slicing) explicitly.

using System;

namespace pbrt
{
    public static unsafe partial class pbrt
    {
        public static Point3f OffsetRayOrigin(Point3fi pi, Normal3f n, Vector3f w)
        {
            // Find vector _offset_ to corner of error bounds and compute initial _po_
            Float d = Dot(Abs(n), pi.Error());
            Vector3f offset = d * (Vector3f)n;
            if (Dot(w, n) < 0)
                offset = -offset;
            Point3f po = (Point3f)pi + offset;

            // Round offset point _po_ away from _p_
            for (int i = 0; i < 3; ++i)
            {
                if (offset[i] > 0)
                    po[i] = NextFloatUp(po[i]);
                else if (offset[i] < 0)
                    po[i] = NextFloatDown(po[i]);
            }

            return po;
        }

        public static Ray SpawnRay(Point3fi pi, Normal3f n, Float time, Vector3f d)
        {
            return new Ray(OffsetRayOrigin(pi, n, d), d, time);
        }

        public static Ray SpawnRayTo(Point3fi pFrom, Normal3f n, Float time, Point3f pTo)
        {
            Vector3f d = pTo - (Point3f)pFrom;
            return SpawnRay(pFrom, n, time, d);
        }

        public static Ray SpawnRayTo(Point3fi pFrom, Normal3f nFrom, Float time,
                                     Point3fi pTo, Normal3f nTo)
        {
            Point3f pf = OffsetRayOrigin(pFrom, nFrom, (Point3f)pTo - (Point3f)pFrom);
            Point3f pt = OffsetRayOrigin(pTo, nTo, pf - (Point3f)pTo);
            return new Ray(pf, pt - pf, time);
        }
    }


    // Ray Definition
    public unsafe struct Ray
    {
        // Ray Public Methods
        public bool HasNaN() { return (o.HasNaN() || d.HasNaN()); }

        public Ray(Point3f o, Vector3f d, Float time = 0f, Medium medium = null)
        {
            this.o = o;
            this.d = d;
            this.time = time;
            this.medium = medium;
        }

        public Point3f Apply(Float t) { return o + d * t; }

        public Ray(RayDifferential r)
        {
            o = r.o;
            d = r.d;
            time = r.time;
            medium = r.medium;
        }

        public override string ToString()
        {
            return StringPrintf("[ o: %s d: %s time: %f, medium: %s ]", o, d, time, medium);
        }

        // Ray Public Members
        public Point3f o;
        public Vector3f d;
        public Float time;
        public Medium medium;
    }

    // RayDifferential Definition
    public unsafe struct RayDifferential
    {
        // RayDifferential Public Methods
        public RayDifferential(Point3f o, Vector3f d, Float time = 0f, Medium medium = null)
        {
            this.o = o;
            this.d = d;
            this.time = time;
            this.medium = medium;
            hasDifferentials = false;
            rxOrigin = default;
            ryOrigin = default;
            rxDirection = default;
            ryDirection = default;
        }

        public RayDifferential(Ray ray)
            : this(ray.o, ray.d, ray.time, ray.medium)
        { }

        public static implicit operator Ray(RayDifferential r) { return new Ray(r); }

        public Point3f Apply(Float t) { return o + d * t; }

        public void ScaleDifferentials(Float s)
        {
            rxOrigin = o + (rxOrigin - o) * s;
            ryOrigin = o + (ryOrigin - o) * s;
            rxDirection = d + (rxDirection - d) * s;
            ryDirection = d + (ryDirection - d) * s;
        }

        public bool HasNaN()
        {
            return o.HasNaN() || d.HasNaN() ||
                   (hasDifferentials && (rxOrigin.HasNaN() || ryOrigin.HasNaN() ||
                                         rxDirection.HasNaN() || ryDirection.HasNaN()));
        }

        public override string ToString()
        {
            return StringPrintf("[ ray: %s differentials: %s xo: %s xd: %s yo: %s yd: %s ]",
                                new Ray(this), hasDifferentials ? "true" : "false",
                                rxOrigin, rxDirection, ryOrigin, ryDirection);
        }

        // RayDifferential Public Members
        public Point3f o;
        public Vector3f d;
        public Float time;
        public Medium medium;
        public bool hasDifferentials;
        public Point3f rxOrigin, ryOrigin;
        public Vector3f rxDirection, ryDirection;
    }

    // Ray Inline Functions

}  // namespace pbrt
