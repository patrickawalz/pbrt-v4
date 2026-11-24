// (not in pbrt-v4) hairtessellator.cs
//
// Strands as triangles: where each cubic curve of a strand is cut, and the tubes or ribbons of triangles through the cuts.
//
// The cuts are adaptive as those of LuxCoreRender's strands are (by the bend of the curve and the change of its thickness,
// with no part for the camera), with the three things that its test lets through seen to: a part is judged at its quarters
// and not at its middle alone (a part that bends one way and then the other can have its middle on the chord), the error
// is in radii of the strand both for the distance from the chord and for the thickness (one number, one meaning), and a
// part whose ends differ in direction by more than the angle is always cut. A part of the curve from t0 to t1 is cut in
// two halves, up to maxDepth times, while
//
//   the tangents at its ends are more than `angle` apart, or
//   the curve at one of its quarters is farther from the chord than `error` radii, or
//   the radius at one of its quarters is more than `error` radii from the radius in a line between the ends.

using System;
using System.Collections.Generic;

namespace pbrt
{
    // HairTessellator Definition
    public static class HairTessellator
    {
        // HairTessellator Public Methods

        // The parameters at which the cubic curve of the Bezier values p (and radii r) is cut, from 0 to 1, in rising order
        public static List<double> Adaptive(SdfVector[] p, double[] r, double angle, double error, int maxDepth)
        {
            List<double> cuts = new List<double>();
            cuts.Add(0);
            Cut(p, r, Math.Cos(angle * Math.PI / 180), error, maxDepth, 0, 1, cuts);
            cuts.Add(1);
            return cuts;
        }

        public static List<double> Uniform(int steps)
        {
            List<double> cuts = new List<double>();
            for (int i = 0; i <= steps; ++i)
            {
                cuts.Add((double)i / steps);
            }
            return cuts;
        }

        public static SdfVector Point(SdfVector[] p, double t)
        {
            double s = 1 - t;
            return p[0] * (s * s * s) + p[1] * (3 * t * s * s) + p[2] * (3 * t * t * s) + p[3] * (t * t * t);
        }

        public static SdfVector Tangent(SdfVector[] p, double t)
        {
            double s = 1 - t;
            return (p[1] - p[0]) * (3 * s * s) + (p[2] - p[1]) * (6 * t * s) + (p[3] - p[2]) * (3 * t * t);
        }

        public static double Value(double[] g, double t)
        {
            double s = 1 - t;
            return g[0] * (s * s * s) + g[1] * (3 * t * s * s) + g[2] * (3 * t * t * s) + g[3] * (t * t * t);
        }

        // The rings of a strand as triangles, added to the mesh: at each ring (center, tangent, radius, v: its place along the
        // strand in [0, 1]) `sides` points around the centerline for a tube (2 for a ribbon: across the centerline, along the
        // first direction of the frame), in a frame that is carried along the strand without twist. The normals are away from
        // the centerline (of a ribbon: across it and its width); uv: around (or across) and along the strand.
        public static void AddStrand(List<SdfVector> centers, List<SdfVector> tangents, List<double> radii, List<double> along, int sides, List<Float> positions,
                                     List<Float> normals, List<Float> uvs, List<int> indices)
        {
            bool ribbon = sides == 2;
            int perRing = ribbon ? 2 : sides + 1;
            int first = positions.Count / 3;
            SdfVector across = default;
            for (int ring = 0; ring < centers.Count; ++ring)
            {
                SdfVector tangent = tangents[ring].LengthSquared() > 0 ? tangents[ring].Normalized() : new SdfVector(0, 0, 1);
                // (the direction across of the ring before, less its part along this tangent)
                across = ring == 0 ? RibbonBuilder.OrthogonalTo(tangent).Normalized() : across - tangent * SdfVector.Dot(across, tangent);
                across = across.LengthSquared() > 1e-24 ? across.Normalized() : RibbonBuilder.OrthogonalTo(tangent).Normalized();
                SdfVector other = SdfVector.Cross(tangent, across);
                for (int k = 0; k < perRing; ++k)
                {
                    SdfVector offset, normal;
                    double u;
                    if (ribbon)
                    {
                        offset = across * (k == 0 ? -radii[ring] : radii[ring]);
                        normal = other;
                        u = k;
                    }
                    else
                    {
                        double turn = 2 * Math.PI * k / sides;
                        normal = across * Math.Cos(turn) + other * Math.Sin(turn);
                        offset = normal * radii[ring];
                        u = (double)k / sides;
                    }
                    SdfVector position = centers[ring] + offset;
                    positions.Add((Float)position.x);
                    positions.Add((Float)position.y);
                    positions.Add((Float)position.z);
                    normals.Add((Float)normal.x);
                    normals.Add((Float)normal.y);
                    normals.Add((Float)normal.z);
                    uvs.Add((Float)u);
                    uvs.Add((Float)along[ring]);
                }
            }
            for (int ring = 0; ring + 1 < centers.Count; ++ring)
            {
                for (int k = 0; k + 1 < perRing; ++k)
                {
                    int a = first + ring * perRing + k, b = a + 1, c = a + perRing, d = c + 1;
                    indices.Add(a);
                    indices.Add(b);
                    indices.Add(d);
                    indices.Add(a);
                    indices.Add(d);
                    indices.Add(c);
                }
            }
        }

        // HairTessellator Private Methods
        private static void Cut(SdfVector[] p, double[] r, double cosine, double error, int depth, double t0, double t1, List<double> cuts)
        {
            if (depth <= 0)
            {
                return;
            }
            SdfVector start = Point(p, t0), end = Point(p, t1), chord = end - start;
            double length = chord.Length();
            double r0 = Math.Max(0, Value(r, t0)), r1 = Math.Max(0, Value(r, t1));
            double allowed = error * Math.Max(0.5 * (r0 + r1), 1e-12);
            SdfVector d0 = Tangent(p, t0), d1 = Tangent(p, t1);
            bool cut = d0.LengthSquared() > 0 && d1.LengthSquared() > 0 && SdfVector.Dot(d0.Normalized(), d1.Normalized()) < cosine;
            for (int quarter = 1; quarter <= 3 && !cut; ++quarter)
            {
                double fraction = 0.25 * quarter, t = t0 + (t1 - t0) * fraction;
                SdfVector from = Point(p, t) - start;
                double distance = length > 0 ? SdfVector.Cross(chord, from).Length() / length : from.Length();
                cut = distance > allowed || Math.Abs(Value(r, t) - (r0 + (r1 - r0) * fraction)) > allowed;
            }
            if (!cut)
            {
                return;
            }
            double middle = 0.5 * (t0 + t1);
            Cut(p, r, cosine, error, depth - 1, t0, middle, cuts);
            cuts.Add(middle);
            Cut(p, r, cosine, error, depth - 1, middle, t1, cuts);
        }
    }
}
