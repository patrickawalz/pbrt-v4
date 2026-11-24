// (not in pbrt-v4) hairtessellator_test.cs
//
// Tests of the cuts of the strands (hairtessellator.cs) and of the triangles through them.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static class HairTessellatorTests
    {
        // The farthest that the curve is from the straight pieces between the cuts, in radii of the strand
        private static double Worst(SdfVector[] p, double[] r, List<double> cuts)
        {
            double worst = 0;
            for (int i = 0; i + 1 < cuts.Count; ++i)
            {
                SdfVector start = HairTessellator.Point(p, cuts[i]), chord = HairTessellator.Point(p, cuts[i + 1]) - start;
                for (int k = 1; k < 40; ++k)
                {
                    double t = cuts[i] + (cuts[i + 1] - cuts[i]) * k / 40;
                    double distance = SdfVector.Cross(chord, HairTessellator.Point(p, t) - start).Length() / chord.Length();
                    worst = Math.Max(worst, distance / HairTessellator.Value(r, t));
                }
            }
            return worst;
        }

        [TEST("HairTessellator", "CutsFollowTheBend")]
        public static void CutsFollowTheBend()
        {
            double[] r = { 0.01, 0.01, 0.01, 0.01 };
            // A straight curve is not cut
            SdfVector[] straight = { new SdfVector(0, 0, 0), new SdfVector(1, 0, 0), new SdfVector(2, 0, 0), new SdfVector(3, 0, 0) };
            CHECK_EQ(HairTessellator.Adaptive(straight, r, 10, 0.1, 6).Count, 2);
            // A curve that bends one way and then the other, with its middle on the chord and its end tangents alike, is cut
            SdfVector[] wave = { new SdfVector(0, 0, 0), new SdfVector(1, 1, 0), new SdfVector(2, -1, 0), new SdfVector(3, 0, 0) };
            SdfVector middle = HairTessellator.Point(wave, 0.5);
            CHECK_LT(Math.Abs(middle.y), 1e-12);
            List<double> cuts = HairTessellator.Adaptive(wave, r, 10, 0.1, 8);
            Console.WriteLine("hair tessellator: the wave in " + (cuts.Count - 1) + " parts, at most " + Worst(wave, r, cuts) + " radii from the curve");
            CHECK_GT(cuts.Count, 8);
            // (the error is kept between the cuts: a little more than at the quarters where it is tested)
            CHECK_LT(Worst(wave, r, cuts), 0.15);
            for (int i = 0; i + 1 < cuts.Count; ++i)
            {
                CHECK_LT(cuts[i], cuts[i + 1]);
            }
            // A thicker strand needs fewer parts for the same error in radii, and a larger error fewer too
            double[] thick = { 0.1, 0.1, 0.1, 0.1 };
            CHECK_LT(HairTessellator.Adaptive(wave, thick, 10, 0.1, 8).Count, cuts.Count);
            CHECK_LT(HairTessellator.Adaptive(wave, r, 10, 1, 8).Count, cuts.Count);
            // A straight strand whose radius swells in the middle is cut for its thickness
            double[] swell = { 0.01, 0.2, 0.2, 0.01 };
            CHECK_GT(HairTessellator.Adaptive(straight, swell, 10, 0.1, 6).Count, 4);
        }

        [TEST("HairTessellator", "TubesAreAroundTheCenterline")]
        public static void TubesAreAroundTheCenterline()
        {
            SdfVector[] p = { new SdfVector(0, 0, 0), new SdfVector(1, 1, 0.5), new SdfVector(2, -1, 0.2), new SdfVector(3, 0, -0.4) };
            double[] r = { 0.05, 0.04, 0.03, 0.01 };
            foreach (int sides in new int[] { 2, 6 })
            {
                List<double> cuts = HairTessellator.Adaptive(p, r, 10, 0.1, 6);
                List<SdfVector> centers = new List<SdfVector>(), tangents = new List<SdfVector>();
                List<double> radii = new List<double>(), along = new List<double>();
                foreach (double t in cuts)
                {
                    centers.Add(HairTessellator.Point(p, t));
                    tangents.Add(HairTessellator.Tangent(p, t));
                    radii.Add(HairTessellator.Value(r, t));
                    along.Add(t);
                }
                List<Float> positions = new List<Float>(), normals = new List<Float>(), uvs = new List<Float>();
                List<int> indices = new List<int>();
                HairTessellator.AddStrand(centers, tangents, radii, along, sides, positions, normals, uvs, indices);
                int perRing = sides == 2 ? 2 : sides + 1;
                CHECK_EQ(positions.Count, 3 * perRing * cuts.Count);
                CHECK_EQ(indices.Count, 6 * (perRing - 1) * (cuts.Count - 1));
                for (int ring = 0; ring < cuts.Count; ++ring)
                {
                    SdfVector tangent = tangents[ring].Normalized();
                    for (int k = 0; k < perRing; ++k)
                    {
                        int at = 3 * (ring * perRing + k);
                        SdfVector offset = new SdfVector(positions[at], positions[at + 1], positions[at + 2]) - centers[ring];
                        SdfVector normal = new SdfVector(normals[at], normals[at + 1], normals[at + 2]);
                        // At the radius from the centerline, across it, with a normal of unit length across it too
                        CHECK_LT(Math.Abs(offset.Length() - radii[ring]), 1e-6);
                        CHECK_LT(Math.Abs(SdfVector.Dot(offset, tangent)), 1e-6);
                        CHECK_LT(Math.Abs(normal.Length() - 1), 1e-5);
                        CHECK_LT(Math.Abs(SdfVector.Dot(normal, tangent)), 1e-5);
                    }
                }
                foreach (int index in indices)
                {
                    CHECK_GE(index, 0);
                    CHECK_LT(index, perRing * cuts.Count);
                }
            }
        }
    }
}
