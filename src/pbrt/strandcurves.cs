// (not in pbrt-v4) strandcurves.cs
//
// The shapes "curve" (pbrt-v4) and "curves" (RenderMan's, shapes_curves.cs) made of the strands "ribbon3" and "rocap" in place of
// the Curve of pbrt-v4. Both read their parameters as before and come to cubic Bezier segments with a width at each end, a type
// and (for ribbons) a normal at each end; a segment then becomes
//
//   type          "look" "surface" (the default)              "look" "pbrt"                         "engine" "pbrt"
//   flat          ribbon3, look "facing" (the same picture)   the same                              Curve
//   cylinder      rocap: the tube                             ribbon3, look "cylinder" (a picture)  Curve
//   ribbon        ribbon3: the surface across the normals     ribbon3, look "curves" (a picture)    Curve
//
// The Curve of pbrt-v4 is a strip that faces each ray: it is the flat type; its cylinder type is that strip with the normal of a
// round strand, and its ribbon type the strip narrowed by the normal, which is too narrow for a ray tilted along the strand.
// "rocap" and "ribbon3" are the tube and the ribbon themselves, can be lights (the looks that are strips cannot), and are what
// the types stand for. "look" "pbrt" keeps the pictures of pbrt-v4 on the new shapes; "engine" "pbrt" the Curve itself.
//
//   "string normals" "slerp" (the default, the rule of pbrt-v4) or "linear" (a "varying" normal of RenderMan): how the normal of a
//   ribbon goes from one end of a segment to the other (Ribbon3Normals).
//   "bool solidanglesampling" (true): of the strands that are lights.
//   "splitdepth" (3): a segment is 2^splitdepth shapes, as with the Curve: the same strand in shapes with tighter bounds, which is
//   what makes very many thin strands fast. For strands that are lights, 0 gives one shape to a segment, which samples better.

using System;
using System.Collections.Generic;

namespace pbrt
{
    // StrandCurveOptions Definition
    public sealed class StrandCurveOptions
    {
        public bool pbrtEngine;
        public bool pbrtLook;
        public Ribbon3Normals normals;
        public bool solidAngleSampling;
        public BezierSurfaceSettings ribbonSettings;
        public BezierSurfaceSettings rocapSettings;
        public Transform renderFromObject;
        public Transform objectFromRender;
        public bool reverseOrientation;

        // diagonal: of the bounds of the control points of the shape (object space)
        public static StrandCurveOptions Read(ParameterDictionary parameters, string shape, Transform renderFromObject, Transform objectFromRender, bool reverseOrientation,
                                              double diagonal, FileLoc loc)
        {
            StrandCurveOptions options = new StrandCurveOptions();
            string engine = parameters.GetOneString("engine", "strands");
            if (engine != "strands" && engine != "pbrt")
            {
                pbrt.ErrorExit(loc, shape + ": \"engine\" is \"strands\" or \"pbrt\".");
            }
            string look = parameters.GetOneString("look", "surface");
            if (look != "surface" && look != "pbrt")
            {
                pbrt.ErrorExit(loc, shape + ": \"look\" is \"surface\" or \"pbrt\".");
            }
            string normals = parameters.GetOneString("normals", "slerp");
            if (normals != "slerp" && normals != "linear")
            {
                pbrt.ErrorExit(loc, shape + ": \"normals\" is \"slerp\" or \"linear\".");
            }
            options.pbrtEngine = engine == "pbrt";
            options.pbrtLook = look == "pbrt";
            options.normals = normals == "linear" ? Ribbon3Normals.Linear : Ribbon3Normals.Slerp;
            options.solidAngleSampling = parameters.GetOneBool("solidanglesampling", true);
            options.renderFromObject = renderFromObject;
            options.objectFromRender = objectFromRender;
            options.reverseOrientation = reverseOrientation;
            options.ribbonSettings = new BezierSurfaceSettings();
            options.ribbonSettings.renderFromObject = renderFromObject;
            options.ribbonSettings.objectFromRender = objectFromRender;
            options.ribbonSettings.reverseOrientation = reverseOrientation;
            options.ribbonSettings.transformSwapsHandedness = renderFromObject.SwapsHandedness();
            options.rocapSettings = new BezierSurfaceSettings();
            options.rocapSettings.renderFromObject = renderFromObject;
            options.rocapSettings.objectFromRender = objectFromRender;
            options.rocapSettings.reverseOrientation = reverseOrientation;
            options.rocapSettings.transformSwapsHandedness = renderFromObject.SwapsHandedness();
            // (as "rocap": the least distance of a hit, a millionth of the size)
            options.rocapSettings.epsilon = 1e-6 * diagonal;
            return options;
        }
    }

    // StrandCurves Definition
    public static class StrandCurves
    {
        // The shapes of one cubic Bezier segment (four control points, the widths at its two ends, its type, and for a ribbon the
        // normals at its two ends), added to the shapes
        public static void Add(pstd.vector<Shape> shapes, StrandCurveOptions options, Point3f[] c, Float w0, Float w1, CurveType type, Normal3f[] norm, int splitDepth,
                               Allocator alloc)
        {
            if (options.pbrtEngine)
            {
                pstd.vector<Shape> made = Curve.CreateCurve(options.renderFromObject, options.objectFromRender, options.reverseOrientation, new pstd.span<Point3f>(c), w0, w1,
                                                            type, norm != null ? new pstd.span<Normal3f>(norm) : default, splitDepth, alloc);
                for (int i = 0; i < (int)made.size(); ++i)
                {
                    shapes.push_back(made[i]);
                }
                return;
            }
            SdfVector[] points = new SdfVector[4];
            for (int k = 0; k < 4; ++k)
            {
                points[k] = new SdfVector(c[k].x, c[k].y, c[k].z);
            }
            // The half width, in a line from one end to the other
            double h0 = 0.5 * w0, h1 = 0.5 * w1;
            double[] halves = { h0, h0 + (h1 - h0) / 3, h0 + 2 * (h1 - h0) / 3, h1 };
            // The segment in 2^splitDepth shapes, as the Curve of pbrt-v4 is: shapes with tighter bounds (the same strand)
            int pieces = 1 << Math.Max(0, Math.Min(6, splitDepth));
            if (type == CurveType.Cylinder && !options.pbrtLook)
            {
                for (int i = 0; i < pieces; ++i)
                {
                    // The part of the cubic curve from a to b: its control values are the blossoms (a, a, a), (a, a, b), (a, b, b)
                    // and (b, b, b)
                    double a = (double)i / pieces, b = (double)(i + 1) / pieces;
                    SdfVector[] part = { Blossom(points, a, a, a), Blossom(points, a, a, b), Blossom(points, a, b, b), Blossom(points, b, b, b) };
                    double[] radii = { Blossom(halves, a, a, a), Blossom(halves, a, a, b), Blossom(halves, a, b, b), Blossom(halves, b, b, b) };
                    shapes.push_back(new RocapShape(options.rocapSettings, new RocapSegment(part, radii, 0, 1)) { SolidAngleSampling = options.solidAngleSampling });
                }
                return;
            }
            SdfVector n0, n1;
            if (type == CurveType.Ribbon && norm != null)
            {
                n0 = new SdfVector(norm[0].x, norm[0].y, norm[0].z);
                n1 = new SdfVector(norm[1].x, norm[1].y, norm[1].z);
            }
            else
            {
                // (the strips that face the ray have no use for a normal)
                SdfVector chord = points[3] - points[0];
                n0 = RibbonBuilder.OrthogonalTo(chord.LengthSquared() > 0 ? chord : new SdfVector(1, 0, 0));
                n1 = n0;
            }
            List<Ribbon3Curve> curves = new List<Ribbon3Curve>();
            curves.Add(new Ribbon3Curve(points, halves, n0, n1, options.normals, null, null, 0, 1));
            bool facing = type != CurveType.Ribbon || options.pbrtLook;
            foreach (Ribbon3Shape shape in Ribbons3.Shapes(options.ribbonSettings, curves, 0, options.solidAngleSampling, true, facing, type == CurveType.Ribbon && facing,
                                                           type == CurveType.Cylinder, pieces))
            {
                shapes.push_back(shape);
            }
        }

        // The blossom of the cubic Bezier values at (t1, t2, t3): the algorithm of de Casteljau with one parameter for each step
        private static SdfVector Blossom(SdfVector[] p, double t1, double t2, double t3)
        {
            SdfVector a = p[0] * (1 - t1) + p[1] * t1, b = p[1] * (1 - t1) + p[2] * t1, c = p[2] * (1 - t1) + p[3] * t1;
            SdfVector d = a * (1 - t2) + b * t2, e = b * (1 - t2) + c * t2;
            return d * (1 - t3) + e * t3;
        }

        private static double Blossom(double[] p, double t1, double t2, double t3)
        {
            double a = p[0] * (1 - t1) + p[1] * t1, b = p[1] * (1 - t1) + p[2] * t1, c = p[2] * (1 - t1) + p[3] * t1;
            double d = a * (1 - t2) + b * t2, e = b * (1 - t2) + c * t2;
            return d * (1 - t3) + e * t3;
        }

        // The length of the diagonal of the bounds of the points
        public static double Diagonal(List<Point3f> points)
        {
            if (points.Count == 0)
            {
                return 0;
            }
            double lx = points[0].x, ly = points[0].y, lz = points[0].z, hx = lx, hy = ly, hz = lz;
            for (int i = 1; i < points.Count; ++i)
            {
                lx = Math.Min(lx, points[i].x);
                ly = Math.Min(ly, points[i].y);
                lz = Math.Min(lz, points[i].z);
                hx = Math.Max(hx, points[i].x);
                hy = Math.Max(hy, points[i].y);
                hz = Math.Max(hz, points[i].z);
            }
            return Math.Sqrt((hx - lx) * (hx - lx) + (hy - ly) * (hy - ly) + (hz - lz) * (hz - lz));
        }
    }
}
