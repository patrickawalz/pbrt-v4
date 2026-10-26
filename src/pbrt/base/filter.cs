// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// base/filter.h

using System;

namespace pbrt
{

    // Filter Definition (TaggedPointer<BoxFilter, GaussianFilter, MitchellFilter, LanczosSincFilter, TriangleFilter>)
    public abstract unsafe partial class Filter
    {
        // Filter Interface
        public abstract Vector2f Radius();
        public abstract Float Evaluate(Point2f p);
        public abstract Float Integral();
        public abstract FilterSample Sample(Point2f u);
        public abstract override string ToString();
    }

}  // namespace pbrt
