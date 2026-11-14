// (not in pbrt-v4) uvchannels_test.cs
//
// The texture coordinate sets of a mesh after the first (uvchannels.cs):
//   - a channel equal to the first gives the first: the same coordinates and tangents;
//   - the tangents of any channel are the change of the point per unit of its coordinates (a step over the primitive
//     moves the point by dpdu du + dpdv dv of that channel), on triangles and bilinear patches, and through an instance;
//   - a channel by name, by number, and one that the mesh does not have (the first);
//   - the "uv" mapping on a channel: its coordinates, their differentials, and the step of a finite difference.

using System;
using testing;

namespace pbrt
{
    public static unsafe class UVChannelsTests
    {
        static TriangleMesh Mesh(Point2f[] first, Point2f[][] others, int[] names)
        {
            Point3f[] p = new Point3f[] { new Point3f(0, 0, 0), new Point3f(2, 0.3f, 0.1f), new Point3f(0.4f, 1.5f, 0.7f) };
            TriangleMesh mesh = new TriangleMesh(new Transform(), false, new pstd.vector<int>(new int[] { 0, 1, 2 }), new pstd.vector<Point3f>(p),
                                                 new pstd.vector<Vector3f>(), new pstd.vector<Normal3f>(),
                                                 first != null ? new pstd.vector<Point2f>(first) : new pstd.vector<Point2f>(), new pstd.vector<int>(), null);
            mesh.uvChannels = new UVChannels(others, names);
            return mesh;
        }

        static SurfaceInteraction Hit(TriangleMesh mesh, Float b0, Float b1)
        {
            return Triangle.InteractionFromIntersection(mesh, 0, new TriangleIntersection(b0, b1, 1 - b0 - b1, 1), 0, new Vector3f(0, 0, 1));
        }

        // How far the step between two hits is from dpdu du + dpdv dv of a channel, over the length of the step
        static double TangentError(SurfaceInteraction a, SurfaceInteraction b, int channel, int nameId)
        {
            Vector3f dpdu, dpdv;
            Point2f uvA = a.UVChannel(channel, nameId, &dpdu, &dpdv);
            Point2f uvB = b.UVChannel(channel, nameId, null, null);
            Vector3f step = b.p() - a.p();
            Vector3f predicted = (uvB[0] - uvA[0]) * dpdu + (uvB[1] - uvA[1]) * dpdv;
            return Length(step - predicted) / Length(step);
        }

        [TEST("UVChannels", "Triangle")]
        public static void UVChannels_Triangle()
        {
            Point2f[] first = new Point2f[] { new Point2f(0.1f, 0.2f), new Point2f(0.9f, 0.3f), new Point2f(0.3f, 0.8f) };
            // The same as the first; an affine function of it; unrelated; and one with a name
            Point2f[] affine = new Point2f[3];
            for (int i = 0; i < 3; ++i)
            {
                affine[i] = new Point2f(2 * first[i][0] - 0.5f * first[i][1] + 0.3f, 0.7f * first[i][0] + 1.5f * first[i][1] - 1);
            }
            Point2f[] other = new Point2f[] { new Point2f(0.7f, 0.1f), new Point2f(0.2f, 0.4f), new Point2f(0.6f, 0.95f) };
            int detail = UVChannelNames.Id("detail");
            foreach (bool withFirst in new bool[] { true, false })
            {
                TriangleMesh mesh = Mesh(withFirst ? first : null, new Point2f[][] { withFirst ? first : null, affine, other, other },
                                         new int[] { -1, -1, -1, detail });
                if (!withFirst)
                {
                    mesh.uvChannels = new UVChannels(new Point2f[][] { affine, other, other }, new int[] { -1, -1, detail });
                }
                SurfaceInteraction a = Hit(mesh, 0.2f, 0.3f), b = Hit(mesh, 0.27f, 0.36f);
                int firstCopy = withFirst ? 1 : -1, affineChannel = withFirst ? 2 : 1, otherChannel = withFirst ? 3 : 2;
                if (withFirst)
                {
                    Vector3f dpdu, dpdv;
                    Point2f uv = a.UVChannel(firstCopy, -1, &dpdu, &dpdv);
                    EXPECT_LT(Length(uv - a.uv), 1e-6);
                    EXPECT_LT(Length(dpdu - a.dpdu) / Length(a.dpdu), 1e-5);
                    EXPECT_LT(Length(dpdv - a.dpdv) / Length(a.dpdv), 1e-5);
                    // The affine function of the first channel, at the hit
                    Point2f expected = new Point2f(2 * a.uv[0] - 0.5f * a.uv[1] + 0.3f, 0.7f * a.uv[0] + 1.5f * a.uv[1] - 1);
                    EXPECT_LT(Length(a.UVChannel(affineChannel, -1, null, null) - expected), 1e-5);
                }
                EXPECT_LT(TangentError(a, b, 0, -1), 1e-4);
                EXPECT_LT(TangentError(a, b, affineChannel, -1), 1e-4);
                EXPECT_LT(TangentError(a, b, otherChannel, -1), 1e-4);
                EXPECT_LT(TangentError(a, b, 0, detail), 1e-4);
                // By name: the unrelated channel
                EXPECT_LT(Length(a.UVChannel(0, detail, null, null) - a.UVChannel(otherChannel, -1, null, null)), 1e-7);
                // A channel that is not there: the first
                EXPECT_LT(Length(a.UVChannel(17, -1, null, null) - a.uv), 1e-7);
                EXPECT_LT(Length(a.UVChannel(0, UVChannelNames.Id("no such channel"), null, null) - a.uv), 1e-7);

                // Through an instance: the tangents are those of the transformed interaction
                Transform instance = pbrt.Translate(new Vector3f(1, 2, 3)) * pbrt.Rotate(35, new Vector3f(0.3f, 1, 0.2f)) * pbrt.Scale(1.5f, 0.7f, 2);
                SurfaceInteraction ta = instance.Apply(a), tb = instance.Apply(b);
                EXPECT_LT(TangentError(ta, tb, otherChannel, -1), 1e-4);
                Vector3f du, dv, tdu, tdv;
                a.UVChannel(otherChannel, -1, &du, &dv);
                ta.UVChannel(otherChannel, -1, &tdu, &tdv);
                EXPECT_LT(Length(instance.Apply(du) - tdu) / Length(tdu), 1e-5);
                EXPECT_LT(Length(instance.Apply(dv) - tdv) / Length(tdv), 1e-5);
            }
        }

        [TEST("UVChannels", "BilinearPatch")]
        public static void UVChannels_BilinearPatch()
        {
            Point3f[] p = new Point3f[] { new Point3f(0, 0, 0), new Point3f(2, 0.1f, 0.2f), new Point3f(0.2f, 1.5f, 0.1f), new Point3f(2.3f, 1.8f, 0.6f) };
            Point2f[] other = new Point2f[] { new Point2f(0.1f, 0.05f), new Point2f(0.8f, 0.2f), new Point2f(0.25f, 0.9f), new Point2f(0.95f, 0.85f) };
            BilinearPatchMesh mesh = new BilinearPatchMesh(new Transform(), false, new pstd.vector<int>(new int[] { 0, 1, 2, 3 }), new pstd.vector<Point3f>(p),
                                                           new pstd.vector<Normal3f>(), new pstd.vector<Point2f>(), new pstd.vector<int>(), null, null);
            mesh.uvChannels = new UVChannels(new Point2f[][] { other }, new int[] { -1 });
            SurfaceInteraction a = BilinearPatch.InteractionFromIntersection(mesh, 0, new Point2f(0.3f, 0.4f), 0, new Vector3f(0, 0, 1));
            SurfaceInteraction b = BilinearPatch.InteractionFromIntersection(mesh, 0, new Point2f(0.302f, 0.403f), 0, new Vector3f(0, 0, 1));
            // A patch is not flat and its coordinates are not affine: the tangents are those at the point, to first order
            EXPECT_LT(TangentError(a, b, 0, -1), 5e-3);
            EXPECT_LT(TangentError(a, b, 1, -1), 5e-3);
            Point2f expected = Lerp(0.3f, Lerp(0.4f, other[0], other[2]), Lerp(0.4f, other[1], other[3]));
            EXPECT_LT(Length(a.UVChannel(1, -1, null, null) - expected), 1e-6);
        }

        [TEST("UVChannels", "Mapping")]
        public static void UVChannels_Mapping()
        {
            Point2f[] first = new Point2f[] { new Point2f(0.1f, 0.2f), new Point2f(0.9f, 0.3f), new Point2f(0.3f, 0.8f) };
            Point2f[] other = new Point2f[] { new Point2f(0.7f, 0.1f), new Point2f(0.2f, 0.4f), new Point2f(0.6f, 0.95f) };
            TriangleMesh mesh = Mesh(first, new Point2f[][] { other }, new int[] { UVChannelNames.Id("second") });
            SurfaceInteraction a = Hit(mesh, 0.2f, 0.3f), b = Hit(mesh, 0.23f, 0.34f);
            Float su = 3, sv = 0.5f, du = 0.25f, dv = -2;
            foreach (bool byName in new bool[] { false, true })
            {
                UVMapping mapping = byName ? new UVMapping(su, sv, du, dv, 0, UVChannelNames.Id("second")) : new UVMapping(su, sv, du, dv, 1, -1);
                Point2f uvA = a.UVChannel(1, -1, null, null), uvB = b.UVChannel(1, -1, null, null);
                TextureEvalContext ctx = new TextureEvalContext(a);
                // The differentials of the pixel: the step to the other hit, and half of it the other way
                ctx.dpdx = b.p() - a.p();
                ctx.dpdy = -0.5f * (b.p() - a.p());
                TexCoord2D c = mapping.Map(ctx);
                EXPECT_LT(Math.Abs(c.st[0] - (su * uvA[0] + du)), 1e-5);
                EXPECT_LT(Math.Abs(c.st[1] - (sv * uvA[1] + dv)), 1e-5);
                EXPECT_LT(Math.Abs(c.dsdx - su * (uvB[0] - uvA[0])), 1e-5);
                EXPECT_LT(Math.Abs(c.dtdx - sv * (uvB[1] - uvA[1])), 1e-5);
                EXPECT_LT(Math.Abs(c.dsdy + 0.5f * su * (uvB[0] - uvA[0])), 1e-5);
                EXPECT_LT(Math.Abs(c.dtdy + 0.5f * sv * (uvB[1] - uvA[1])), 1e-5);
                // A context moved to the other hit, as a finite difference moves it, maps to the other hit
                TextureEvalContext moved = ctx;
                moved.shift = b.p() - a.p();
                TexCoord2D m = mapping.Map(moved);
                EXPECT_LT(Math.Abs(m.st[0] - (su * uvB[0] + du)), 1e-5);
                EXPECT_LT(Math.Abs(m.st[1] - (sv * uvB[1] + dv)), 1e-5);
            }
            // The mapping of the first channel is what it was
            TexCoord2D stock = new UVMapping(su, sv, du, dv).Map(new TextureEvalContext(a));
            EXPECT_EQ(su * a.uv[0] + du, stock.st[0]);
            EXPECT_EQ(sv * a.uv[1] + dv, stock.st[1]);
        }
    }
}
