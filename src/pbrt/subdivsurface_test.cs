// (not in pbrt-v4) subdivsurface_test.cs
//
// Tests of the subdivision surfaces ray traced with Bezier clipping (subdivsurface.cs).

using System;
using System.Collections.Generic;
using OpenSubdiv.Far;
using testing;

namespace pbrt
{
    public static unsafe class SubdivisionSurfaceTests
    {
        private static SubdivisionSurfaceInput Cube(double creaseWeight)
        {
            SubdivisionSurfaceInput input = new SubdivisionSurfaceInput();
            input.positions = new double[]
            {
                -1, -1, -1,   1, -1, -1,   1, 1, -1,   -1, 1, -1,
                -1, -1, 1,    1, -1, 1,    1, 1, 1,    -1, 1, 1
            };
            input.faceVertexCounts = new int[] { 4, 4, 4, 4, 4, 4 };
            input.indices = new int[]
            {
                0, 3, 2, 1,   4, 5, 6, 7,   0, 1, 5, 4,   1, 2, 6, 5,   2, 3, 7, 6,   3, 0, 4, 7
            };
            if (creaseWeight > 0)
            {
                input.creaseVertexPairs = new int[] { 4, 5, 5, 6, 6, 7, 7, 4 };
                input.creaseWeights = new float[] { (float)creaseWeight, (float)creaseWeight, (float)creaseWeight, (float)creaseWeight };
            }
            input.isolationLevel = 3;
            return input;
        }

        private static BezierSurfaceSettings Settings()
        {
            BezierSurfaceSettings settings = new BezierSurfaceSettings();
            settings.renderFromObject = new Transform();
            settings.objectFromRender = new Transform();
            settings.epsilon = 1e-5;
            return settings;
        }

        [TEST("Subdiv", "RegularPatchesConvertToBezierExactly")]
        public static void RegularPatchesConvertToBezierExactly()
        {
            SubdivisionPatches patches = SubdivisionSurface.BuildPatches(Cube(0));
            PatchTable table = patches.table;
            int regular = 0, gregory = 0;
            double worstRegular = 0, worstGregory = 0;
            for (int a = 0; a < table.GetNumPatchArrays(); ++a)
            {
                for (int p = 0; p < table.GetNumPatches(a); ++p)
                {
                    if (!SubdivisionSurface.ToBezier(patches, a, p, out BezierPatch4 bezier))
                    {
                        continue;
                    }
                    SubdivisionPatchSurface surface = new SubdivisionPatchSurface(patches, a, p);
                    double worst = 0;
                    for (int i = 0; i <= 4; ++i)
                    {
                        for (int j = 0; j <= 4; ++j)
                        {
                            double u = i / 4.0, v = j / 4.0;
                            surface.Evaluate(u, v, out SdfVector exact, out SdfVector du, out SdfVector dv, out Point2f uv, out SdfVector ns);
                            worst = Math.Max(worst, (bezier.Evaluate(u, v) - exact).Length());
                        }
                    }
                    if (table.GetPatchArrayDescriptor(a).GetType() == PatchDescriptor.Type.REGULAR)
                    {
                        ++regular;
                        worstRegular = Math.Max(worstRegular, worst);
                    }
                    else
                    {
                        ++gregory;
                        worstGregory = Math.Max(worstGregory, worst);
                    }
                }
            }
            Console.WriteLine("regular " + regular + " worst " + worstRegular + ", gregory " + gregory + " worst " + worstGregory);
            CHECK_GT(regular, 0);
            CHECK_GT(gregory, 0);
            CHECK_LT(worstRegular, 1e-12);
            // The averaged inner points deviate a little from the Gregory surface near the extraordinary vertices
            CHECK_LT(worstGregory, 0.05);
        }

        // A triangle soup of the exact surface
        private static List<SdfVector[]> Tessellate(SubdivisionPatches patches, int n)
        {
            List<SdfVector[]> triangles = new List<SdfVector[]>();
            PatchTable table = patches.table;
            for (int a = 0; a < table.GetNumPatchArrays(); ++a)
            {
                for (int p = 0; p < table.GetNumPatches(a); ++p)
                {
                    SubdivisionPatchSurface surface = new SubdivisionPatchSurface(patches, a, p);
                    SdfVector[] grid = new SdfVector[(n + 1) * (n + 1)];
                    for (int j = 0; j <= n; ++j)
                    {
                        for (int i = 0; i <= n; ++i)
                        {
                            surface.Evaluate((double)i / n, (double)j / n, out grid[j * (n + 1) + i], out SdfVector du, out SdfVector dv, out Point2f uv, out SdfVector ns);
                        }
                    }
                    for (int j = 0; j < n; ++j)
                    {
                        for (int i = 0; i < n; ++i)
                        {
                            SdfVector p00 = grid[j * (n + 1) + i], p10 = grid[j * (n + 1) + i + 1], p01 = grid[(j + 1) * (n + 1) + i], p11 = grid[(j + 1) * (n + 1) + i + 1];
                            triangles.Add(new SdfVector[] { p00, p10, p01 });
                            triangles.Add(new SdfVector[] { p10, p11, p01 });
                        }
                    }
                }
            }
            return triangles;
        }

        public static bool MoellerHit(SdfVector o, SdfVector d, SdfVector[] tri, out double t)
        {
            return Moeller(o, d, tri, out t);
        }

        private static bool Moeller(SdfVector o, SdfVector d, SdfVector[] tri, out double t)
        {
            t = 0;
            SdfVector e1 = tri[1] - tri[0], e2 = tri[2] - tri[0];
            SdfVector pv = SdfVector.Cross(d, e2);
            double det = SdfVector.Dot(e1, pv);
            if (Math.Abs(det) < 1e-18)
            {
                return false;
            }
            SdfVector s = o - tri[0];
            double u = SdfVector.Dot(s, pv) / det;
            if (u < 0 || u > 1)
            {
                return false;
            }
            SdfVector q = SdfVector.Cross(s, e1);
            double v = SdfVector.Dot(d, q) / det;
            if (v < 0 || u + v > 1)
            {
                return false;
            }
            t = SdfVector.Dot(e2, q) / det;
            return t > 0;
        }

        private static void RaysAgainstBruteForce(SubdivisionSurfaceInput input, int rayCount, out int total, out int cracks, out int extra, out double worstT)
        {
            SubdivisionPatches patches = SubdivisionSurface.BuildPatches(input);
            pstd.vector<Shape> shapes = SubdivisionSurface.CreateShapes(Settings(), patches);
            List<SdfVector[]> triangles = Tessellate(patches, 24);
            Random random = new Random(5);
            total = 0;
            cracks = 0;
            extra = 0;
            worstT = 0;
            for (int k = 0; k < rayCount; ++k)
            {
                SdfVector dir = new SdfVector(random.NextDouble() - 0.5, random.NextDouble() - 0.5, random.NextDouble() - 0.5).Normalized();
                SdfVector target = new SdfVector(random.NextDouble() * 2.4 - 1.2, random.NextDouble() * 2.4 - 1.2, random.NextDouble() * 2.4 - 1.2);
                SdfVector origin = target - dir * 6;
                double expected = double.MaxValue;
                foreach (SdfVector[] tri in triangles)
                {
                    if (Moeller(origin, dir, tri, out double t) && t < expected)
                    {
                        expected = t;
                    }
                }
                Ray ray = new Ray(new Point3f((Float)origin.x, (Float)origin.y, (Float)origin.z), new Vector3f((Float)dir.x, (Float)dir.y, (Float)dir.z));
                double actual = double.MaxValue;
                for (int s = 0; s < (int)shapes.size(); ++s)
                {
                    pstd.optional<ShapeIntersection> hit = shapes[s].Intersect(ray);
                    if (hit && hit.value().tHit < actual)
                    {
                        actual = hit.value().tHit;
                    }
                }
                bool expectedHit = expected < double.MaxValue, actualHit = actual < double.MaxValue;
                if (expectedHit)
                {
                    ++total;
                    if (!actualHit)
                    {
                        ++cracks;
                    }
                    else
                    {
                        worstT = Math.Max(worstT, Math.Abs(actual - expected));
                    }
                }
                else if (actualHit)
                {
                    ++extra;
                }
            }
        }

        [TEST("Subdiv", "RaysFindTheSameSurfaceAsTheTessellation")]
        public static void RaysFindTheSameSurfaceAsTheTessellation()
        {
            RaysAgainstBruteForce(Cube(0), 1500, out int total, out int cracks, out int extra, out double worstT);
            Console.WriteLine("cube: hits " + total + " missed " + cracks + " extra " + extra + " worst t " + worstT);
            CHECK_GT(total, 400);
            CHECK_LT(cracks, 2);
            CHECK_LT(extra, 4);
            CHECK_LT(worstT, 5e-3);
        }

        [TEST("Subdiv", "CreasedCubeHasNoCracks")]
        public static void CreasedCubeHasNoCracks()
        {
            RaysAgainstBruteForce(Cube(2.5), 1500, out int total, out int cracks, out int extra, out double worstT);
            Console.WriteLine("creased cube: hits " + total + " missed " + cracks + " extra " + extra + " worst t " + worstT);
            CHECK_GT(total, 400);
            CHECK_LT(cracks, 2);
            CHECK_LT(extra, 4);
            CHECK_LT(worstT, 5e-3);
        }

        [TEST("Subdiv", "AreaSamplingIsUnbiasedAndMatchesTheTessellation")]
        public static void AreaSamplingIsUnbiasedAndMatchesTheTessellation()
        {
            SubdivisionPatches patches = SubdivisionSurface.BuildPatches(Cube(0));
            pstd.vector<Shape> shapes = SubdivisionSurface.CreateShapes(Settings(), patches);
            double reference = 0;
            foreach (SdfVector[] tri in Tessellate(patches, 24))
            {
                reference += 0.5 * SdfVector.Cross(tri[1] - tri[0], tri[2] - tri[0]).Length();
            }
            double areaSum = 0, worstMean = 0;
            Random random = new Random(3);
            int onSurface = 0, samples = 0;
            for (int k = 0; k < (int)shapes.size(); ++k)
            {
                Shape shape = shapes[k];
                double area = shape.Area();
                areaSum += area;
                // E[1 / pdf] over the samples is the area of the shape
                double mean = 0;
                int count = 4000;
                for (int i = 0; i < count; ++i)
                {
                    pstd.optional<ShapeSample> ss = shape.Sample(new Point2f((Float)random.NextDouble(), (Float)random.NextDouble()));
                    if (ss)
                    {
                        mean += 1.0 / ss.value().pdf;
                        ++samples;
                        // The normal faces away from the center of the cube
                        Point3f p = ss.value().intr.p();
                        Normal3f n = ss.value().intr.n;
                        if (p.x * n.x + p.y * n.y + p.z * n.z > 0)
                        {
                            ++onSurface;
                        }
                    }
                }
                mean /= count;
                worstMean = Math.Max(worstMean, Math.Abs(mean - area) / area);
            }
            Console.WriteLine("area sum " + areaSum + " tessellated " + reference + " worst |E[1/pdf] - area| / area " + worstMean + " outward normals " + onSurface + " / " + samples);
            CHECK_LT(Math.Abs(areaSum - reference) / reference, 0.01);
            CHECK_LT(worstMean, 0.05);
            CHECK_EQ(onSurface, samples);
        }

        [TEST("Subdiv", "SolidAnglePdfMatchesTheSampler")]
        public static void SolidAnglePdfMatchesTheSampler()
        {
            SubdivisionPatches patches = SubdivisionSurface.BuildPatches(Cube(0));
            pstd.vector<Shape> shapes = SubdivisionSurface.CreateShapes(Settings(), patches);
            Point3f reference = new Point3f(3.0f, 2.0f, -2.5f);
            ShapeSampleContext ctx = new ShapeSampleContext(new Point3fi(reference, new Vector3f(0, 0, 0)), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), 0);
            Random random = new Random(11);
            int compared = 0, mismatched = 0;
            for (int k = 0; k < (int)shapes.size(); ++k)
            {
                for (int i = 0; i < 60; ++i)
                {
                    pstd.optional<ShapeSample> ss = shapes[k].Sample(ctx, new Point2f((Float)random.NextDouble(), (Float)random.NextDouble()));
                    if (!ss)
                    {
                        continue;
                    }
                    Vector3f wi = pbrt.Normalize(ss.value().intr.p() - reference);
                    Ray ray = ctx.SpawnRay(wi);
                    // The pdf is that of the first hit of the patch: skip the samples on the far side of the patch
                    pstd.optional<ShapeIntersection> hit = shapes[k].Intersect(ray);
                    // and the grazing ones, where the pdf of the solid angle changes much with the point
                    if (!hit || pbrt.Distance(hit.value().intr.p(), ss.value().intr.p()) > 1e-3 || pbrt.AbsDot(ss.value().intr.n, wi) < 0.05)
                    {
                        continue;
                    }
                    ++compared;
                    Float pdf = shapes[k].PDF(ctx, wi);
                    if (Math.Abs(pdf - ss.value().pdf) > 0.1 * ss.value().pdf)
                    {
                        ++mismatched;
                        if (mismatched < 6)
                        {
                            Console.WriteLine("mismatch shape " + k + " pdf " + pdf + " sample pdf " + ss.value().pdf + " distance " + pbrt.Distance(hit.value().intr.p(), ss.value().intr.p()));
                        }
                    }
                }
            }
            Console.WriteLine("solid angle pdf: compared " + compared + " mismatched " + mismatched);
            CHECK_GT(compared, 100);
            // The Gregory patches are traced as a proxy that deviates by 2.5e-4 from the surface, and the Jacobian changes quickly at their corners
            CHECK_LT(mismatched, compared / 200);
        }

        // The largest gap between the displaced points that two patches give for the same point of their shared edge
        private static double SeamGap(string x, string y, string z, bool tangentSpace, out int pairs)
        {
            PatchDisplacement displacement = new PatchDisplacement();
            displacement.mode = PatchDisplacementMode.Vector;
            displacement.tangentSpace = tangentSpace;
            displacement.smoothNormals = true;
            displacement.field = new ShellVectorProcedural(ShellExpression.Parse(x), ShellExpression.Parse(y), ShellExpression.Parse(z), 6, 1);
            SubdivisionPatches patches = SubdivisionSurface.BuildPatches(Cube(0));
            pstd.vector<Shape> shapes = displacement.Apply(SubdivisionSurface.CreateShapes(Settings(), patches), Settings());
            System.Reflection.MethodInfo displaced = typeof(DisplacedPatchShape).GetMethod("Displaced", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            System.Reflection.FieldInfo surfaceField = typeof(DisplacedPatchShape).GetField("surface", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            int count = (int)shapes.size();
            double worst = 0;
            pairs = 0;
            // The samples of the borders of every patch, keyed by their position on the base surface
            Dictionary<string, List<double[]>> byPosition = new Dictionary<string, List<double[]>>();
            for (int a = 0; a < count; ++a)
            {
                PatchSurface surface = (PatchSurface)surfaceField.GetValue(shapes[a]);
                for (int e = 0; e < 4; ++e)
                {
                    for (int k = 0; k <= 64; ++k)
                    {
                        double t = k / 64.0;
                        double u = e == 0 ? t : e == 1 ? 1 : e == 2 ? t : 0, v = e == 0 ? 0 : e == 1 ? t : e == 2 ? 1 : t;
                        surface.Evaluate(u, v, out SdfVector p, out SdfVector du, out SdfVector dv, out Point2f uv, out SdfVector ns);
                        string key = Math.Round(p.x * 1e6) + "," + Math.Round(p.y * 1e6) + "," + Math.Round(p.z * 1e6);
                        if (!byPosition.TryGetValue(key, out List<double[]> list))
                        {
                            list = new List<double[]>();
                            byPosition[key] = list;
                        }
                        list.Add(new double[] { a, u, v });
                    }
                }
            }
            foreach (List<double[]> list in byPosition.Values)
            {
                for (int i = 0; i < list.Count; ++i)
                {
                    for (int j = i + 1; j < list.Count; ++j)
                    {
                        if ((int)list[i][0] == (int)list[j][0])
                        {
                            continue;
                        }
                        SdfVector qa = (SdfVector)displaced.Invoke(shapes[(int)list[i][0]], new object[] { list[i][1], list[i][2] });
                        SdfVector qb = (SdfVector)displaced.Invoke(shapes[(int)list[j][0]], new object[] { list[j][1], list[j][2] });
                        worst = Math.Max(worst, (qa - qb).Length());
                        ++pairs;
                    }
                }
            }
            return worst;
        }

        [TEST("Subdiv", "DisplacedSeamGaps")]
        public static void DisplacedSeamGaps()
        {
            double constantObject = SeamGap("0.05", "0.03", "0.04", false, out int p1);
            double borderZeroObject = SeamGap("0.04*sin(2*pi*u)*sin(2*pi*v)", "0.04*sin(2*pi*u)*sin(2*pi*v)", "0.07*sin(4*pi*u)*sin(4*pi*v)", false, out int p2);
            double borderZeroTangent = SeamGap("0.04*sin(2*pi*u)*sin(2*pi*v)", "0.04*sin(2*pi*u)*sin(2*pi*v)", "0.07*sin(4*pi*u)*sin(4*pi*v)", true, out int p3);
            double cosineObject = SeamGap("0.06*cos(2*pi*u)+0.03", "0.06*cos(2*pi*v)", "0.05*cos(2*pi*u)*cos(2*pi*v)+0.03", false, out int p4);
            double cosineTangent = SeamGap("0.06*cos(2*pi*u)+0.03", "0.06*cos(2*pi*v)", "0.05*cos(2*pi*u)*cos(2*pi*v)+0.03", true, out int p5);
            Console.WriteLine("seam gaps: constant object " + constantObject + " (" + p1 + " points), border-zero object " + borderZeroObject + " (" + p2 + "), border-zero tangent " + borderZeroTangent + " (" + p3 +
                              "), cosine object " + cosineObject + " (" + p4 + "), cosine tangent " + cosineTangent + " (" + p5 + ")");
            CHECK_GT(p1, 100);
            // The seams are welded, so the field need not be continuous across them
            CHECK_LT(constantObject, 1e-6);
            CHECK_LT(borderZeroObject, 1e-6);
            CHECK_LT(borderZeroTangent, 1e-6);
            CHECK_LT(cosineObject, 1e-6);
            CHECK_LT(cosineTangent, 1e-6);
        }

        // Rays grazing the silhouette near a corner: the point of every hit must be on the ray and face the viewer (a conservative child hit once had swapped uv offsets)
        [TEST("Subdiv", "SilhouetteHitsLieOnTheRay")]
        public static void SilhouetteHitsLieOnTheRay()
        {
            SubdivisionPatches patches = SubdivisionSurface.BuildPatches(Cube(0));
            pstd.vector<Shape> shapes = SubdivisionSurface.CreateShapes(Settings(), patches);
            SdfVector eye = new SdfVector(2.6, 2.2, -3.2);
            SdfVector focus = new SdfVector(0.896, 0.441, 0.0478);
            SdfVector forward = (focus - eye).Normalized();
            SdfVector right = SdfVector.Cross(new SdfVector(0, 1, 0), forward).Normalized();
            SdfVector up = SdfVector.Cross(forward, right);
            double scale = Math.Tan(1.5 * Math.PI / 180);
            int bad = 0, total = 0;
            for (int py = 0; py < 60; ++py)
            {
                for (int px = 0; px < 60; ++px)
                {
                    double sx = (px - 30) / 30.0 * scale, sy = (30 - py) / 30.0 * scale;
                    SdfVector dir = (forward + right * sx + up * sy).Normalized();
                    Ray ray = new Ray(new Point3f((Float)eye.x, (Float)eye.y, (Float)eye.z), new Vector3f((Float)dir.x, (Float)dir.y, (Float)dir.z));
                    pstd.optional<ShapeIntersection> best = default;
                    for (int k = 0; k < (int)shapes.size(); ++k)
                    {
                        pstd.optional<ShapeIntersection> hit = shapes[k].Intersect(ray);
                        if (hit && (!best || hit.value().tHit < best.value().tHit))
                        {
                            best = hit;
                        }
                    }
                    if (!best)
                    {
                        continue;
                    }
                    ++total;
                    Normal3f n = best.value().intr.n;
                    double length = Math.Sqrt((double)n.x * n.x + (double)n.y * n.y + (double)n.z * n.z);
                    Vector3f wo = best.value().intr.wo;
                    double facing = n.x * wo.x + n.y * wo.y + n.z * wo.z;
                    if (double.IsNaN(length) || length < 0.5 || facing <= 0)
                    {
                        ++bad;
                        if (bad < 12)
                        {
                            Console.WriteLine("bad hit px " + px + " py " + py + " t " + best.value().tHit + " n " + n.x + " " + n.y + " " + n.z + " facing " + facing + " uv " + best.value().intr.uv.x + " " + best.value().intr.uv.y + " face " + best.value().intr.faceIndex + " |hit - ray point| " + Math.Sqrt(Math.Pow(best.value().intr.p().x - (eye.x + dir.x * best.value().tHit), 2) + Math.Pow(best.value().intr.p().y - (eye.y + dir.y * best.value().tHit), 2) + Math.Pow(best.value().intr.p().z - (eye.z + dir.z * best.value().tHit), 2)) + " p " + best.value().intr.p().x + " " + best.value().intr.p().y + " " + best.value().intr.p().z);
                        }
                    }
                }
            }
            Console.WriteLine("silhouette: hits " + total + " bad " + bad);
            CHECK_GT(total, 1000);
            CHECK_EQ(bad, 0);
        }
    }
}
