// (not in pbrt-v4) shapes_curves.cs
//
// RiCurves on top of the curves of pbrt-v4 (Curve, shapes.cs): many curves in one shape, linear or cubic, any of the RenderMan bases, periodic or
// not, with the widths and the normals given per vertex of the interpolation (varying). Every segment becomes the cubic Bezier segment of
// pbrt-v4 (a linear segment is a cubic Bezier with its control points on the line), so the intersection and the flat, ribbon and cylinder
// treatments are those of pbrt-v4.
//
//   Shape "curves" "integer nvertices" [...] "point3 P" [...] ("string type" "linear" | "cubic") ("string wrap" "nonperiodic" | "periodic")
//         ("string basis" "bezier") ("integer step" 3) ("float width" [...] | "float constantwidth" w) ("normal N" [...]) ("string style" "flat")
//   type:   RenderMan's type of the curve; the values "flat", "ribbon" and "cylinder" of pbrt-v4 are taken as the style (and the curve is cubic)
//   width:  one per varying value: nonperiodic cubic curves have (vertices - 4) / step + 2, periodic ones vertices / step, linear ones one per vertex
//           (RenderMan's "width" and pbrt's "widths" are both read)
//   N:      the same count as the widths; with normals the curves are ribbons
//   style:  "flat" (faces the ray, RenderMan's own), "ribbon" or "cylinder"

using System;
using System.Collections.Generic;

namespace pbrt
{
    public static class RenderManCurves
    {
        public static pstd.vector<Shape> Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation, ParameterDictionary parameters,
                                                FileLoc loc, Allocator alloc)
        {
            pstd.vector<Shape> shapes = new pstd.vector<Shape>();
            List<int> nvertices = parameters.GetIntArray("nvertices");
            List<Point3f> points = parameters.GetPoint3fArray("P");
            if (nvertices.Count == 0 || points.Count == 0)
            {
                pbrt.Error(loc, "curves: \"nvertices\" and \"P\" are required.");
                return shapes;
            }
            string type = parameters.GetOneString("type", parameters.GetOneString("curvetype", "cubic"));
            string style = parameters.GetOneString("style", "flat");
            if (type == "flat" || type == "ribbon" || type == "cylinder")
            {
                style = type;
                type = "cubic";
            }
            if (type != "linear" && type != "cubic")
            {
                pbrt.Error(loc, "curves: \"type\" must be \"linear\" or \"cubic\".");
                return shapes;
            }
            bool linear = type == "linear";
            string wrap = parameters.GetOneString("wrap", "nonperiodic");
            if (wrap != "periodic" && wrap != "nonperiodic")
            {
                pbrt.Error(loc, "curves: \"wrap\" must be \"periodic\" or \"nonperiodic\".");
                return shapes;
            }
            bool periodic = wrap == "periodic";
            CurveType curveType;
            if (style == "flat")
            {
                curveType = CurveType.Flat;
            }
            else if (style == "ribbon")
            {
                curveType = CurveType.Ribbon;
            }
            else if (style == "cylinder")
            {
                curveType = CurveType.Cylinder;
            }
            else
            {
                pbrt.Error(loc, "curves: unknown style \"" + style + "\".");
                return shapes;
            }
            string basisName = parameters.GetOneString("basis", "bezier");
            double[,] basis = null;
            if (!linear && !PatchBases.TryGet(basisName, out basis))
            {
                pbrt.Error(loc, "curves: unknown basis \"" + basisName + "\".");
                return shapes;
            }
            int step = parameters.GetOneInt("step", parameters.GetOneInt("vstep", PatchBases.DefaultStep(basisName)));
            if (!linear && step < 1)
            {
                pbrt.Error(loc, "curves: \"step\" must be positive.");
                return shapes;
            }
            List<Float> widths = parameters.GetFloatArray("width");
            if (widths.Count == 0)
            {
                widths = parameters.GetFloatArray("widths");
            }
            Float constantWidth = parameters.GetOneFloat("constantwidth", 1);
            List<Normal3f> normals = parameters.GetNormal3fArray("N");
            if (normals.Count > 0)
            {
                curveType = CurveType.Ribbon;
            }
            else if (curveType == CurveType.Ribbon)
            {
                pbrt.Error(loc, "curves: ribbon curves need the normals \"N\".");
                return shapes;
            }
            int splitDepth = parameters.GetOneInt("splitdepth", 3);
            double[,] bezier;
            PatchBases.TryGet("bezier", out bezier);
            double[,] toBezier = linear ? null : PatchBases.Multiply(PatchBases.Inverse(bezier), basis);

            int vertexBase = 0, varyingBase = 0;
            for (int c = 0; c < nvertices.Count; ++c)
            {
                int nv = nvertices[c];
                int segments;
                int varying;
                if (linear)
                {
                    segments = periodic ? nv : nv - 1;
                    varying = nv;
                    if (nv < 2 || (periodic && nv < 3))
                    {
                        pbrt.Error(loc, "curves: curve " + c + " has too few vertices.");
                        return new pstd.vector<Shape>();
                    }
                }
                else if (periodic)
                {
                    if (nv < 4 || nv % step != 0)
                    {
                        pbrt.Error(loc, "curves: periodic curve " + c + " needs at least 4 vertices, a multiple of the step.");
                        return new pstd.vector<Shape>();
                    }
                    segments = nv / step;
                    varying = segments;
                }
                else
                {
                    if (nv < 4 || (nv - 4) % step != 0)
                    {
                        pbrt.Error(loc, "curves: curve " + c + " needs 4 + n * step vertices.");
                        return new pstd.vector<Shape>();
                    }
                    segments = (nv - 4) / step + 1;
                    varying = segments + 1;
                }
                if (vertexBase + nv > points.Count)
                {
                    pbrt.Error(loc, "curves: \"P\" has fewer points than \"nvertices\" adds up to.");
                    return new pstd.vector<Shape>();
                }
                if (widths.Count > 0 && varyingBase + varying > widths.Count)
                {
                    pbrt.Error(loc, "curves: \"width\" needs one value per varying vertex (" + varying + " for curve " + c + ").");
                    return new pstd.vector<Shape>();
                }
                if (normals.Count > 0 && varyingBase + varying > normals.Count)
                {
                    pbrt.Error(loc, "curves: \"N\" needs one normal per varying vertex (" + varying + " for curve " + c + ").");
                    return new pstd.vector<Shape>();
                }
                for (int s = 0; s < segments; ++s)
                {
                    Point3f[] bez = new Point3f[4];
                    if (linear)
                    {
                        Point3f a = points[vertexBase + s], b = points[vertexBase + (s + 1) % nv];
                        for (int k = 0; k < 4; ++k)
                        {
                            bez[k] = pbrt.Lerp(k / 3.0f, a, b);
                        }
                    }
                    else
                    {
                        Point3f[] g = new Point3f[4];
                        for (int k = 0; k < 4; ++k)
                        {
                            g[k] = points[vertexBase + (s * step + k) % nv];
                        }
                        for (int i = 0; i < 4; ++i)
                        {
                            double x = 0, y = 0, z = 0;
                            for (int k = 0; k < 4; ++k)
                            {
                                x += toBezier[i, k] * g[k].x;
                                y += toBezier[i, k] * g[k].y;
                                z += toBezier[i, k] * g[k].z;
                            }
                            bez[i] = new Point3f((Float)x, (Float)y, (Float)z);
                        }
                    }
                    int v0 = varyingBase + s, v1 = varyingBase + (s + 1) % varying;
                    Float w0 = widths.Count > 0 ? widths[v0] : constantWidth, w1 = widths.Count > 0 ? widths[v1] : constantWidth;
                    pstd.span<Normal3f> normalSpan = default;
                    if (normals.Count > 0)
                    {
                        normalSpan = new pstd.span<Normal3f>(new Normal3f[] { normals[v0], normals[v1] });
                    }
                    pstd.vector<Shape> made = Curve.CreateCurve(renderFromObject, objectFromRender, reverseOrientation, new pstd.span<Point3f>(bez), w0, w1, curveType,
                                                                normalSpan, splitDepth, alloc);
                    for (int i = 0; i < (int)made.size(); ++i)
                    {
                        shapes.push_back(made[i]);
                    }
                }
                vertexBase += nv;
                varyingBase += varying;
            }
            return shapes;
        }
    }
}  // namespace pbrt
