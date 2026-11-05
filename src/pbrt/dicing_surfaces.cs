// (not in pbrt-v4) dicing_surfaces.cs
//
// The parametric surfaces that dicing.cs dices, and the hook that Shape.Create calls for the shapes that have a "texture displacement":
//   AnalyticDicedSurface   a quadric: one patch, (u, v) in [0, 1]^2 as in the intersection routines
//   BezierDicedSurface     the bicubic patches (patch meshes, the teapot, subdivision surfaces and curved patches): one patch each
//   BilinearDicedSurface   the bilinear patches (patchmesh "bilinear", bilinearmesh)
//   NurbsDicedSurface      a NURBS surface: one patch per Bezier cell, trimmed by the trim curves
// The decisions per shape are listed in docs/displacement.md.

using System;
using System.Collections.Generic;

namespace pbrt
{
    public delegate void DicedFunction(double u, double v, out SdfVector p, out SdfVector n);

    public sealed class AnalyticDicedSurface : IDicedSurface
    {
        private readonly DicedFunction function;

        public AnalyticDicedSurface(DicedFunction function)
        {
            this.function = function;
        }

        public int PatchCount
        {
            get { return 1; }
        }

        public bool HasTrim
        {
            get { return false; }
        }

        public void PatchDomain(int patch, out double u0, out double u1, out double v0, out double v1)
        {
            u0 = 0;
            u1 = 1;
            v0 = 0;
            v1 = 1;
        }

        public void Evaluate(int patch, double u, double v, out SdfVector p, out SdfVector n, out Point2f uv)
        {
            function(u, v, out p, out n);
            uv = new Point2f((Float)u, (Float)v);
        }

        public bool IsVisible(int patch, double u, double v)
        {
            return true;
        }

        public int FaceIndex(int patch)
        {
            return 0;
        }
    }

    public sealed class BezierDicedSurface : IDicedSurface
    {
        private readonly List<BezierClipPatchShape> patches;

        public BezierDicedSurface(List<BezierClipPatchShape> patches)
        {
            this.patches = patches;
        }

        public int PatchCount
        {
            get { return patches.Count; }
        }

        public bool HasTrim
        {
            get { return false; }
        }

        public void PatchDomain(int patch, out double u0, out double u1, out double v0, out double v1)
        {
            u0 = 0;
            u1 = 1;
            v0 = 0;
            v1 = 1;
        }

        private static SdfVector NormalOf(PatchSurface surface, double u, double v)
        {
            surface.Evaluate(u, v, out SdfVector p, out SdfVector dpdu, out SdfVector dpdv, out Point2f uv, out SdfVector shading);
            if (shading.LengthSquared() > 0)
            {
                return shading.Normalized();
            }
            SdfVector cross = SdfVector.Cross(dpdu, dpdv);
            double lu = dpdu.LengthSquared(), lv = dpdv.LengthSquared();
            if (cross.LengthSquared() > 1e-18 * lu * lv && lu > 0 && lv > 0)
            {
                return cross.Normalized();
            }
            return new SdfVector(0, 0, 0);
        }

        public void Evaluate(int patch, double u, double v, out SdfVector p, out SdfVector n, out Point2f uv)
        {
            PatchSurface surface = patches[patch].Surface;
            surface.Evaluate(u, v, out p, out SdfVector dpdu, out SdfVector dpdv, out uv, out SdfVector shading);
            n = NormalOf(surface, u, v);
            // At a degenerate point (a collapsed edge of a patch, an extraordinary vertex) the normal is taken a little closer to the center
            for (int attempt = 1; attempt <= 6 && n.LengthSquared() == 0; ++attempt)
            {
                double s = 1e-3 * attempt;
                n = NormalOf(surface, u + (0.5 - u) * s, v + (0.5 - v) * s);
            }
            if (n.LengthSquared() == 0)
            {
                n = new SdfVector(0, 0, 1);
            }
        }

        public bool IsVisible(int patch, double u, double v)
        {
            return true;
        }

        public int FaceIndex(int patch)
        {
            return patches[patch].Surface.FaceIndex();
        }
    }

    public sealed class BilinearDicedSurface : IDicedSurface
    {
        private readonly List<SdfVector[]> corners = new List<SdfVector[]>();   // p00, p10, p01, p11
        private readonly List<Point2f[]> texture = new List<Point2f[]>();       // null without texture coordinates
        private readonly List<int> faces = new List<int>();

        // The patches are in render space (as the BilinearPatchMesh keeps them); the surface is in object space
        public BilinearDicedSurface(List<BilinearPatch> shapes, Transform objectFromRender)
        {
            foreach (BilinearPatch shape in shapes)
            {
                shape.GetCorners(out Point3f[] p, out Point2f[] uv, out int face);
                SdfVector[] c = new SdfVector[4];
                for (int i = 0; i < 4; ++i)
                {
                    Point3f q = objectFromRender.Apply(p[i]);
                    c[i] = new SdfVector(q.x, q.y, q.z);
                }
                corners.Add(c);
                texture.Add(uv);
                faces.Add(face);
            }
        }

        public int PatchCount
        {
            get { return corners.Count; }
        }

        public bool HasTrim
        {
            get { return false; }
        }

        public void PatchDomain(int patch, out double u0, out double u1, out double v0, out double v1)
        {
            u0 = 0;
            u1 = 1;
            v0 = 0;
            v1 = 1;
        }

        private static SdfVector Lerp(double t, SdfVector a, SdfVector b)
        {
            return (1 - t) * a + t * b;
        }

        private static SdfVector Normal(SdfVector[] c, double u, double v)
        {
            SdfVector dpdu = Lerp(v, c[1], c[3]) - Lerp(v, c[0], c[2]);
            SdfVector dpdv = Lerp(u, c[2], c[3]) - Lerp(u, c[0], c[1]);
            SdfVector cross = SdfVector.Cross(dpdu, dpdv);
            if (cross.LengthSquared() > 1e-18 * dpdu.LengthSquared() * dpdv.LengthSquared() && cross.LengthSquared() > 0)
            {
                return cross.Normalized();
            }
            return new SdfVector(0, 0, 0);
        }

        public void Evaluate(int patch, double u, double v, out SdfVector p, out SdfVector n, out Point2f uv)
        {
            SdfVector[] c = corners[patch];
            p = Lerp(u, Lerp(v, c[0], c[2]), Lerp(v, c[1], c[3]));
            n = Normal(c, u, v);
            for (int attempt = 1; attempt <= 6 && n.LengthSquared() == 0; ++attempt)
            {
                double s = 1e-3 * attempt;
                n = Normal(c, u + (0.5 - u) * s, v + (0.5 - v) * s);
            }
            if (n.LengthSquared() == 0)
            {
                n = new SdfVector(0, 0, 1);
            }
            Point2f[] t = texture[patch];
            if (t == null)
            {
                uv = new Point2f((Float)u, (Float)v);
            }
            else
            {
                double s0 = (1 - u) * ((1 - v) * t[0].x + v * t[2].x) + u * ((1 - v) * t[1].x + v * t[3].x);
                double s1 = (1 - u) * ((1 - v) * t[0].y + v * t[2].y) + u * ((1 - v) * t[1].y + v * t[3].y);
                uv = new Point2f((Float)s0, (Float)s1);
            }
        }

        public bool IsVisible(int patch, double u, double v)
        {
            return true;
        }

        public int FaceIndex(int patch)
        {
            return faces[patch];
        }
    }

    public sealed class NurbsDicedSurface : IDicedSurface
    {
        private readonly NurbsSurfaceData surface;
        private readonly TrimRegion trim;

        public NurbsDicedSurface(NurbsSurfaceData surface, TrimRegion trim)
        {
            this.surface = surface;
            this.trim = trim;
        }

        public int PatchCount
        {
            get { return surface.cells.Count; }
        }

        public bool HasTrim
        {
            get { return !trim.IsEmpty; }
        }

        public void PatchDomain(int patch, out double u0, out double u1, out double v0, out double v1)
        {
            NurbsSurfaceData.Cell cell = surface.cells[patch];
            u0 = Math.Max(cell.u0, surface.uMin);
            u1 = Math.Min(cell.u1, surface.uMax);
            v0 = Math.Max(cell.v0, surface.vMin);
            v1 = Math.Min(cell.v1, surface.vMax);
        }

        private bool Frame(double u, double v, double[] p, out SdfVector n)
        {
            double[] pu = new double[3], pv = new double[3];
            surface.Evaluate(u, v, p, pu, pv);
            SdfVector a = new SdfVector(pu[0], pu[1], pu[2]), b = new SdfVector(pv[0], pv[1], pv[2]);
            SdfVector cross = SdfVector.Cross(a, b);
            n = cross;
            if (cross.LengthSquared() > 1e-18 * a.LengthSquared() * b.LengthSquared() && cross.LengthSquared() > 0)
            {
                n = cross.Normalized();
                return true;
            }
            return false;
        }

        public void Evaluate(int patch, double u, double v, out SdfVector p, out SdfVector n, out Point2f uv)
        {
            double[] q = new double[3];
            bool good = Frame(u, v, q, out n);
            p = new SdfVector(q[0], q[1], q[2]);
            double cu = 0.5 * (surface.uMin + surface.uMax), cv = 0.5 * (surface.vMin + surface.vMax);
            for (int attempt = 1; attempt <= 6 && !good; ++attempt)
            {
                double s = 1e-3 * attempt;
                good = Frame(u + (cu - u) * s, v + (cv - v) * s, new double[3], out n);
            }
            if (!good)
            {
                n = new SdfVector(0, 0, 1);
            }
            uv = new Point2f((Float)((u - surface.uMin) / (surface.uMax - surface.uMin)), (Float)((v - surface.vMin) / (surface.vMax - surface.vMin)));
        }

        public bool IsVisible(int patch, double u, double v)
        {
            return trim.Inside(u, v);
        }

        public int FaceIndex(int patch)
        {
            return 0;
        }
    }

    // The hook of Shape.Create
    public static class DicedShapes
    {
        // The shapes that dice: quadrics, patches (bicubic with Bezier clipping or bilinear), NURBS, subdivision surfaces and the teapot
        private static readonly HashSet<string> Diceable = new HashSet<string>
        {
            "sphere", "cylinder", "disk", "cone", "paraboloid", "hyperboloid", "torus", "patch", "patchmesh", "bilinearmesh", "nupatch", "catmarksubdiv",
            "subdivisionmesh", "curvedpatches", "teapot"
        };

        // The shapes that cannot be displaced (no parameterization, a curve, a point, a volume, or a mesh that has its own displacement)
        private static readonly HashSet<string> NotDiceable = new HashSet<string>
        {
            "curve", "curves", "points", "blobby", "volume", "sdf", "ribbon", "rocap", "procedural", "loopsubdiv", "polygon", "generalpolygon",
            "pointspolygons", "pointsgeneralpolygons"
        };

        public static bool TryCreate(string name, Transform renderFromObject, Transform objectFromRender, bool reverseOrientation,
                                     ParameterDictionary parameters, IDictionary<string, FloatTexture> floatTextures, FileLoc loc, Allocator alloc,
                                     out pstd.vector<Shape> shapes)
        {
            shapes = null;
            if (NotDiceable.Contains(name))
            {
                if (parameters.GetTexture("displacement").Length != 0)
                {
                    pbrt.Warning(loc, name + ": \"texture displacement\" is ignored, this shape cannot be displaced by dicing.");
                }
                return false;
            }
            bool teapot = name == "geometry" && parameters.GetOneString("type", "teapot") == "teapot";
            if (!Diceable.Contains(name) && !teapot)
            {
                return false;
            }
            DicingSettings settings = DicingSettings.Parse(name, parameters, floatTextures, renderFromObject, reverseOrientation, loc);
            if (settings == null)
            {
                return false;
            }
            if (parameters.GetOneString("displacement", "none") != "none")
            {
                pbrt.ErrorExit(loc, name + ": \"texture displacement\" (diced) and \"string displacement\" (tessellation-free patch displacement) cannot both be given.");
            }
            pstd.vector<Shape> exact = Shape.CreateExact(name, renderFromObject, objectFromRender, reverseOrientation, parameters, floatTextures, loc, alloc);
            IDicedSurface surface = SurfaceOf(exact, objectFromRender);
            if (surface == null)
            {
                pbrt.ErrorExit(loc, name + ": this shape cannot be displaced by dicing.");
            }
            DicedMesh mesh = Dicer.Dice(surface, settings);
            pbrt.LOG_VERBOSE("Diced %s: %d triangles, largest displacement %f, edge length %f", name, mesh.TriangleCount, mesh.maxDisplacement,
                             mesh.effectiveEdgeLength);
            shapes = Dicer.CreateShapes(mesh, settings, alloc);
            return true;
        }

        public static IDicedSurface SurfaceOf(pstd.vector<Shape> exact, Transform objectFromRender)
        {
            if (exact.empty())
            {
                return null;
            }
            if (exact.size() == 1 && exact[0] is QuadricBase quadric)
            {
                return quadric.CreateDicedSurface();
            }
            if (exact.size() == 1 && exact[0] is NurbsPatch nurbs)
            {
                return nurbs.CreateDicedSurface();
            }
            List<BezierClipPatchShape> bezier = new List<BezierClipPatchShape>();
            List<BilinearPatch> bilinear = new List<BilinearPatch>();
            for (ulong i = 0; i < exact.size(); ++i)
            {
                if (exact[i] is BezierClipPatchShape b)
                {
                    bezier.Add(b);
                }
                else if (exact[i] is BilinearPatch l)
                {
                    bilinear.Add(l);
                }
                else
                {
                    return null;
                }
            }
            if (bezier.Count != 0 && bilinear.Count == 0)
            {
                return new BezierDicedSurface(bezier);
            }
            if (bilinear.Count != 0 && bezier.Count == 0)
            {
                return new BilinearDicedSurface(bilinear, objectFromRender);
            }
            return null;
        }
    }
}  // namespace pbrt
