// (not in pbrt-v4) surfgrad_test.cs
//
// The surface-gradient framework (surfgrad.cs, bumpsurfgrad.cs):
//   - a tangent-space normal map resolves to the normal of pbrt's NormalMap();
//   - a height in the scale-dependent mode resolves to the normal of pbrt's BumpMap() where the surface is flat (BumpMap()
//     adds the H dn/du terms, which the framework leaves out as Blinn did);
//   - a height that is a function of the point gives the same gradient as a volume texture;
//   - a perturbed normal goes to a gradient and back;
//   - the blends of two normal maps (PD, whiteout, UDN, RNM) against their formulas;
//   - a second set of texture coordinates: an affine function of the first, and a channel of the mesh that is the same
//     function, give the same gradient;
//   - the shading geometry rebuilt from the resolved normal.

using System;
using testing;

namespace pbrt
{
    public static unsafe class SurfgradTests
    {
        static Image Constant(Vector3f normal)
        {
            Image image = new Image(PixelFormat.Float, new Point2i(2, 2), new string[] { "R", "G", "B" });
            for (int y = 0; y < 2; ++y)
            {
                for (int x = 0; x < 2; ++x)
                {
                    image.SetChannel(new Point2i(x, y), 0, 0.5f * (normal.x + 1));
                    image.SetChannel(new Point2i(x, y), 1, 0.5f * (normal.y + 1));
                    image.SetChannel(new Point2i(x, y), 2, 0.5f * (normal.z + 1));
                }
            }
            return image;
        }

        static Image Random(ref RNG rng, int size)
        {
            Image image = new Image(PixelFormat.Float, new Point2i(size, size), new string[] { "R", "G", "B" });
            for (int y = 0; y < size; ++y)
            {
                for (int x = 0; x < size; ++x)
                {
                    Vector3f normal = Normalize(new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, 0.4f + rng.Uniform<Float>()));
                    image.SetChannel(new Point2i(x, y), 0, 0.5f * (normal.x + 1));
                    image.SetChannel(new Point2i(x, y), 1, 0.5f * (normal.y + 1));
                    image.SetChannel(new Point2i(x, y), 2, 0.5f * (normal.z + 1));
                }
            }
            return image;
        }

        // A point of a surface with a shading normal that is not the normal of dpdu x dpdv scaled, tangents that are neither
        // unit nor at a right angle, and either handedness
        static NormalBumpEvalContext Context(ref RNG rng, bool rightHanded)
        {
            NormalBumpEvalContext ctx = new NormalBumpEvalContext();
            ctx.p = new Point3f(2 * rng.Uniform<Float>() - 1, 2 * rng.Uniform<Float>() - 1, 2 * rng.Uniform<Float>() - 1);
            ctx.uv = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
            Vector3f n = Normalize(new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f));
            Vector3f a, b;
            CoordinateSystem(n, &a, &b);
            Float angle = 2 * Pi * rng.Uniform<Float>(), skew = 0.5f + 2 * rng.Uniform<Float>();
            Vector3f dpdu = (0.3f + 2 * rng.Uniform<Float>()) * (std.cos(angle) * a + std.sin(angle) * b);
            Vector3f dpdv = (0.3f + 2 * rng.Uniform<Float>()) * (std.cos(angle + skew) * a + std.sin(angle + skew) * b);
            if ((Dot(n, Cross(dpdu, dpdv)) > 0) != rightHanded)
            {
                dpdv = -dpdv;
            }
            ctx.n = (Normal3f)n;
            ctx.shading.n = (Normal3f)n;
            ctx.shading.dpdu = dpdu;
            ctx.shading.dpdv = dpdv;
            ctx.gdpdu = dpdu;
            ctx.gdpdv = dpdv;
            ctx.dpdx = 0.01f * dpdu;
            ctx.dpdy = 0.01f * dpdv;
            ctx.dudx = 0.01f;
            ctx.dvdy = 0.01f;
            return ctx;
        }

        static Normal3f Resolve(BumpStack stack, NormalBumpEvalContext ctx)
        {
            SurfgradFrame frame;
            Vector3f gradient = SurfgradEvaluate(stack, ctx, &frame);
            return ResolveNormalFromSurfaceGradient(gradient, frame);
        }

        [TEST("Surfgrad", "TangentNormalMapIsNormalMap")]
        public static void TangentNormalMapIsNormalMap()
        {
            RNG rng = new RNG(1);
            Image image = Random(ref rng, 8);
            BumpStack stack = new BumpStack();
            BumpLayer layer = stack.Add();
            layer.kind = BumpLayerKind.TangentNormalMap;
            layer.image = image;
            double worst = 0, worstMirrored = 0;
            for (int i = 0; i < 2000; ++i)
            {
                bool rightHanded = (i & 1) == 0;
                NormalBumpEvalContext ctx = Context(ref rng, rightHanded);
                Vector3f dpdu, dpdv;
                NormalMap(image, ctx, &dpdu, &dpdv);
                Vector3f stock = Normalize(Cross(dpdu, dpdv));
                Vector3f mine = (Vector3f)Resolve(stack, ctx);
                if (rightHanded)
                {
                    worst = Math.Max(worst, Length(stock - mine));
                }
                else
                {
                    // NormalMap() takes n x dpdu for the second tangent whichever way v runs; the framework follows v. The
                    // two agree once the map's second component is negated
                    SurfgradFrame frame;
                    SurfgradEvaluate(stack, ctx, &frame);
                    Vector3f m = SampleNormalMap(image, ctx.uv);
                    Vector2f d = TangentSpaceNormalToDerivative(new Vector3f(m.x, -m.y, m.z), false);
                    Vector3f mirrored = (Vector3f)ResolveNormalFromSurfaceGradient(SurfgradFromTBN(d, frame.t, frame.b), frame);
                    worstMirrored = Math.Max(worstMirrored, Length(stock - mirrored));
                    EXPECT_GT(Length(stock - mine), -1);
                }
                // The rebuilt shading geometry gives the resolved normal and keeps the lengths
                SurfgradBumpMap(stack, ctx, &dpdu, &dpdv);
                EXPECT_LT(Length(Normalize(Cross(dpdu, dpdv)) - mine), 2e-5);
                EXPECT_LT(std.abs(Length(dpdu) - Length(ctx.shading.dpdu)), 1e-5f * Length(dpdu));
                EXPECT_LT(std.abs(Length(dpdv) - Length(ctx.shading.dpdv)), 1e-5f * Length(dpdv));
            }
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE surfgrad tangent normal map against NormalMap(): %.2e; with v running the other way, " +
                                                      "against NormalMap() of the mirrored map: %.2e", worst, worstMirrored));
            EXPECT_LT(worst, 2e-5);
            EXPECT_LT(worstMirrored, 2e-5);
        }

        [TEST("Surfgrad", "HeightIsBumpMap")]
        public static void HeightIsBumpMap()
        {
            RNG rng = new RNG(2);
            FloatTexture ofPoint = new FBmTexture(new PointTransformMapping(pbrt.Scale(1.7f, 1.7f, 1.7f)), 3, 0.5f);
            FloatTexture ofUV = new FloatBilerpTexture(new UVMapping(1.3f, 0.8f, 0.1f, 0.2f), 0.1f, 0.5f, -0.3f, 0.4f);
            FloatTexture height = new FloatAddTexture(new FloatScaledTexture(ofPoint, new FloatConstantTexture(0.3f)), ofUV);
            BumpStack stack = new BumpStack();
            BumpLayer layer = stack.Add();
            layer.kind = BumpLayerKind.HeightTexture;
            layer.height = height;
            layer.scaleDependent = true;
            BumpStack volume = new BumpStack();
            BumpLayer volumeLayer = volume.Add();
            volumeLayer.kind = BumpLayerKind.VolumeTexture;
            volumeLayer.height = ofPoint;
            BumpStack surface = new BumpStack();
            BumpLayer surfaceLayer = surface.Add();
            surfaceLayer.kind = BumpLayerKind.HeightTexture;
            surfaceLayer.height = ofPoint;
            surfaceLayer.scaleDependent = true;
            double worst = 0, worstVolume = 0, worstScale = 0;
            for (int i = 0; i < 2000; ++i)
            {
                NormalBumpEvalContext ctx = Context(ref rng, (i & 1) == 0);
                Vector3f dpdu, dpdv;
                BumpMap(new UniversalTextureEvaluator(), height, ctx, &dpdu, &dpdv);
                Vector3f stock = Normalize(Cross(dpdu, dpdv));
                if (Dot(stock, (Vector3f)ctx.shading.n) < 0)
                {
                    stock = -stock;
                }
                worst = Math.Max(worst, Length(stock - (Vector3f)Resolve(stack, ctx)));

                // A function of the point: as a height on the surface and as a volume, the same gradient, in the tangent plane
                SurfgradFrame frame;
                Vector3f asVolume = SurfgradEvaluate(volume, ctx, &frame), asHeight = SurfgradEvaluate(surface, ctx, null);
                worstVolume = Math.Max(worstVolume, Length(asVolume - asHeight) / (Length(asHeight) + 1e-3));
                EXPECT_LT(std.abs(Dot(asVolume, (Vector3f)frame.n)), 1e-5f * (1 + Length(asVolume)));

                // Linear: a layer scaled is its gradient scaled
                surfaceLayer.scale = -2.5f;
                worstScale = Math.Max(worstScale, Length(SurfgradEvaluate(surface, ctx, null) + 2.5f * asHeight) / (Length(asHeight) + 1e-3));
                surfaceLayer.scale = 1;
            }
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE surfgrad height against BumpMap() on a flat surface: %.2e; volume against height of the " +
                                                      "point: %.2e of the gradient; scaled layer: %.2e", worst, worstVolume, worstScale));
            EXPECT_LT(worst, 1e-4);
            EXPECT_LT(worstVolume, 1e-3);
            EXPECT_LT(worstScale, 1e-5);
        }

        [TEST("Surfgrad", "PerturbedNormalAndBlends")]
        public static void PerturbedNormalAndBlends()
        {
            RNG rng = new RNG(3);
            double worst = 0;
            for (int i = 0; i < 2000; ++i)
            {
                NormalBumpEvalContext ctx = Context(ref rng, (i & 1) == 0);
                SurfgradFrame frame = MakeSurfgradFrame(ctx.p, ctx.shading.n, ctx.shading.dpdu, ctx.shading.dpdv, ctx.dpdx, ctx.dpdy);
                Vector3f v = Normalize((Vector3f)frame.n + 0.8f * new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f));
                Vector3f back = (Vector3f)ResolveNormalFromSurfaceGradient(SurfgradFromPerturbedNormal(v, frame), frame);
                worst = Math.Max(worst, Length(back - v));
            }
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE surfgrad perturbed normal to a gradient and back: %.2e", worst));
            EXPECT_LT(worst, 1e-5);

            // Two normal maps on the frame (x, y, z): the resolved normal against the formula of each blend
            NormalBumpEvalContext flat = new NormalBumpEvalContext();
            flat.shading.n = new Normal3f(0, 0, 1);
            flat.shading.dpdu = new Vector3f(1, 0, 0);
            flat.shading.dpdv = new Vector3f(0, 1, 0);
            double worstBlend = 0;
            for (int i = 0; i < 200; ++i)
            {
                Vector3f a = Normalize(new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, 0.5f + rng.Uniform<Float>()));
                Vector3f b = Normalize(new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, 0.5f + rng.Uniform<Float>()));
                foreach (TangentNormalBlend blend in new TangentNormalBlend[] { TangentNormalBlend.PartialDerivative, TangentNormalBlend.Whiteout,
                                                                                TangentNormalBlend.UDN, TangentNormalBlend.Reoriented })
                {
                    BumpStack stack = new BumpStack();
                    BumpLayer first = stack.Add();
                    first.image = Constant(a);
                    BumpLayer second = stack.Add();
                    second.image = Constant(b);
                    second.blend = blend;
                    Vector3f expected;
                    switch (blend)
                    {
                        case TangentNormalBlend.PartialDerivative:
                            expected = new Vector3f(a.x / a.z + b.x / b.z, a.y / a.z + b.y / b.z, 1);
                            break;
                        case TangentNormalBlend.Whiteout:
                            expected = new Vector3f(a.x + b.x, a.y + b.y, a.z * b.z);
                            break;
                        case TangentNormalBlend.UDN:
                            expected = new Vector3f(a.x + b.x, a.y + b.y, a.z);
                            break;
                        default:
                        {
                            // The detail normal rotated by the shortest arc that takes z to the base normal
                            Vector3f axis = Cross(new Vector3f(0, 0, 1), a);
                            Float sine = Length(axis), cosine = a.z;
                            axis = sine > 0 ? axis / sine : new Vector3f(1, 0, 0);
                            expected = b * cosine + Cross(axis, b) * sine + axis * (Dot(axis, b) * (1 - cosine));
                            break;
                        }
                    }
                    worstBlend = Math.Max(worstBlend, Length(Normalize(expected) - (Vector3f)Resolve(stack, flat)));
                }
            }
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE surfgrad blends of two normal maps against their formulas: %.2e", worstBlend));
            EXPECT_LT(worstBlend, 2e-5);
        }

        [TEST("Surfgrad", "UVSets")]
        public static void UVSets()
        {
            // The second coordinates are the first turned by 30 degrees and scaled by 2: its tangents on (x, y, z)
            NormalBumpEvalContext flat = new NormalBumpEvalContext();
            flat.shading.n = new Normal3f(0, 0, 1);
            flat.shading.dpdu = new Vector3f(1, 0, 0);
            flat.shading.dpdv = new Vector3f(0, 1, 0);
            Vector3f m = Normalize(new Vector3f(0.3f, -0.2f, 0.8f));
            BumpStack stack = new BumpStack();
            BumpLayer layer = stack.Add();
            layer.image = Constant(m);
            layer.uvSet = stack.UVSet(UVSetTransform.ScaleRotate(2, 2, Radians(30), 0.1f, 0.2f));
            EXPECT_EQ(1, layer.uvSet);
            Float c = std.cos(Radians(30)), s = std.sin(Radians(30));
            Vector3f t = new Vector3f(c, -s, 0), b = new Vector3f(s, c, 0);
            Vector3f expected = (-m.x / m.z) * t + (-m.y / m.z) * b;
            EXPECT_LT(Length(SurfgradEvaluate(stack, flat, null) - expected), 1e-5);

            // A triangle whose second channel is an affine function of the first: the channel, and the function applied to
            // the first channel, give the same gradient (tangent normal map and height)
            Point2f[] first = new Point2f[] { new Point2f(0.1f, 0.2f), new Point2f(0.9f, 0.3f), new Point2f(0.3f, 0.8f) };
            UVSetTransform affine = UVSetTransform.Identity();
            affine.j00 = 2;
            affine.j01 = -0.5f;
            affine.j10 = 0.7f;
            affine.j11 = 1.5f;
            affine.ou = 0.3f;
            affine.ov = -1;
            Point2f[] second = new Point2f[3];
            for (int i = 0; i < 3; ++i)
            {
                second[i] = affine.Apply(first[i]);
            }
            Point3f[] p = new Point3f[] { new Point3f(0, 0, 0), new Point3f(2, 0.3f, 0.1f), new Point3f(0.4f, 1.5f, 0.7f) };
            TriangleMesh mesh = new TriangleMesh(new Transform(), false, new pstd.vector<int>(new int[] { 0, 1, 2 }), new pstd.vector<Point3f>(p),
                                                 new pstd.vector<Vector3f>(), new pstd.vector<Normal3f>(), new pstd.vector<Point2f>(first),
                                                 new pstd.vector<int>(), null);
            int detail = UVChannelNames.Id("detail");
            mesh.uvChannels = new UVChannels(new Point2f[][] { first, second }, new int[] { -1, detail });
            SurfaceInteraction si = Triangle.InteractionFromIntersection(mesh, 0, new TriangleIntersection(0.2f, 0.3f, 0.5f, 1), 0, new Vector3f(0, 0, 1));
            NormalBumpEvalContext ctx = new NormalBumpEvalContext(si);
            RNG rng = new RNG(4);
            Image image = Random(ref rng, 8);
            FloatTexture height = new FloatBilerpTexture(new UVMapping(1, 1, 0, 0), 0.1f, 0.5f, -0.3f, 0.4f);
            UVSetTransform byNumber = UVSetTransform.Identity(), byName = UVSetTransform.Identity();
            byNumber.channel = 2;
            byName.channelName = detail;
            Vector3f[] gradients = new Vector3f[3];
            UVSetTransform[] ways = new UVSetTransform[] { affine, byNumber, byName };
            for (int way = 0; way < 3; ++way)
            {
                BumpStack sets = new BumpStack();
                BumpLayer normals = sets.Add();
                normals.image = image;
                normals.uvSet = sets.UVSet(ways[way]);
                BumpLayer heights = sets.Add();
                heights.kind = BumpLayerKind.HeightTexture;
                heights.height = height;
                heights.scaleDependent = true;
                heights.uvSet = sets.UVSet(ways[way]);
                EXPECT_EQ(1, heights.uvSet);
                gradients[way] = SurfgradEvaluate(sets, ctx, null);
            }
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE surfgrad second coordinates as a function of the first against the channel of the mesh: " +
                                                      "by number %.2e, by name %.2e of the gradient (%f)", Length(gradients[1] - gradients[0]) / Length(gradients[0]),
                                                      Length(gradients[2] - gradients[0]) / Length(gradients[0]), Length(gradients[0])));
            EXPECT_GT(Length(gradients[0]), 0.05f);
            EXPECT_LT(Length(gradients[1] - gradients[0]), 1e-4f * Length(gradients[0]));
            EXPECT_LT(Length(gradients[2] - gradients[0]), 1e-4f * Length(gradients[0]));
        }
    }
}
