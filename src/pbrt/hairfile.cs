// (not in pbrt-v4) hairfile.cs
//
// Shape "hairfile": the strands of a HAIR file (cyhair.cs) as strand shapes, each strand through the basis of the shape that
// is chosen, with nothing between the file and the shape but the basis (no tessellation).
//
//   "string filename"        the HAIR file
//   "string shape"           "rocap" (the default), "rocylinder", "ribbon3", or "curve" (the Curve of pbrt-v4 and what stands
//                            for it: one for each cubic curve of a strand)
//   "float thicknessscale"   (1) times the thickness of the file, which is the width of a strand
//   "bool colors"            (true) the colors of the file as the colors of the strands ("Cs": for the "strandcolor" texture);
//                            not with "curve"
//
//   "string shape" "mesh"    triangles (hairtessellator.cs), with
//     "string mesh"          "tube" (the default) or "ribbon" (flat, in a frame carried along the strand without twist)
//     "integer sides"        (6) of a tube
//     "string tessellation"  "adaptive" (the default) or "uniform"
//     "float angle"          (10) degrees between the tangents at the ends of a part that is not cut
//     "float error"          (0.1) radii of the strand from the chord, and of its thickness, of a part that is not cut
//     "integer maxdepth"     (6) halvings of a curve
//     "integer steps"        (4) parts of each curve, of "uniform"
//
// and every parameter of the shape that is chosen. Without "basis" the strands are "catmull-rom" with "wrap" "pinned": through
// the points of the file, from the first to the last. The transparency of the file is not used.

using System;
using System.Collections.Generic;

namespace pbrt
{
    // HairFile Definition
    public static class HairFile
    {
        // HairFile Public Methods
        public static pstd.vector<Shape> Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation, ParameterDictionary parameters,
                                                FileLoc loc, Allocator alloc)
        {
            string filename = pbrt.ResolveFilename(parameters.GetOneString("filename", ""));
            string target = parameters.GetOneString("shape", "rocap");
            if (target != "rocap" && target != "rocylinder" && target != "ribbon3" && target != "curve")
            {
                pbrt.ErrorExit(loc, "hairfile: \"shape\" is \"rocap\", \"rocylinder\", \"ribbon3\" or \"curve\".");
            }
            double scale = parameters.GetOneFloat("thicknessscale", 1);
            bool useColors = parameters.GetOneBool("colors", true) && target != "curve";
            HairStrandSet set = CyHair.Read(filename);

            // The parameters of the shape as they were given, and the basis of a strand through its points where none was
            List<ParsedParameter> given = new List<ParsedParameter>(parameters.GetParameterVector());
            given.Reverse();
            bool hasBasis = false, hasWrap = false;
            foreach (ParsedParameter parameter in given)
            {
                hasBasis |= parameter.name == "basis";
                hasWrap |= parameter.name == "wrap";
            }
            if (!hasBasis)
            {
                given.Add(Strings(loc, "basis", "catmull-rom"));
                if (!hasWrap)
                {
                    given.Add(Strings(loc, "wrap", "pinned"));
                }
            }
            // "bool checkselfintersection": one warning for the file with the number of strands that are not sure to be free of
            // self-intersections (strandbasis.cs), not one for each strand
            bool check = parameters.GetOneBool("checkselfintersection", false);
            given.RemoveAll(parameter => parameter.name == "checkselfintersection");
            int crossing = 0;
            string firstCrossing = null;
            StrandBasis basis = null;
            if (target == "curve" || check)
            {
                basis = StrandBasis.Read(new ParameterDictionary(new List<ParsedParameter>(), given, parameters.ColorSpace()), "hairfile", "catmull-rom", 3, loc);
            }

            pstd.vector<Shape> shapes = new pstd.vector<Shape>();
            for (int strand = 0; strand < set.StrandCount; ++strand)
            {
                int first = set.first[strand], count = set.first[strand + 1] - first;
                if (count < 2)
                {
                    continue;
                }
                if (check)
                {
                    double[] px = new double[count], py = new double[count], pz = new double[count];
                    for (int i = 0; i < count; ++i)
                    {
                        px[i] = set.points[3 * (first + i)];
                        py[i] = set.points[3 * (first + i) + 1];
                        pz[i] = set.points[3 * (first + i) + 2];
                    }
                    string reason = basis.Check(count) == null ? basis.Parameterized(px, py, pz).SelfIntersection(px, py, pz) : null;
                    if (reason != null)
                    {
                        ++crossing;
                        firstCrossing ??= "strand " + strand + ": " + reason;
                    }
                }
                pstd.vector<Shape> made;
                if (target == "curve")
                {
                    string problem = basis.Check(count);
                    if (problem != null)
                    {
                        pbrt.ErrorExit(loc, "hairfile: strand " + strand + ": " + problem + ".");
                    }
                    double[] x = new double[count], y = new double[count], z = new double[count], width = new double[count];
                    for (int i = 0; i < count; ++i)
                    {
                        x[i] = set.points[3 * (first + i)];
                        y[i] = set.points[3 * (first + i) + 1];
                        z[i] = set.points[3 * (first + i) + 2];
                        width[i] = scale * (set.thickness != null ? set.thickness[first + i] : set.defaultThickness);
                    }
                    StrandBasis strandBasis = basis.Parameterized(x, y, z);
                    x = strandBasis.Vectors(x, 3);
                    y = strandBasis.Vectors(y, 3);
                    z = strandBasis.Vectors(z, 3);
                    width = strandBasis.Curves(width, 3);
                    for (int curve = 0; curve < x.Length / 4; ++curve)
                    {
                        List<ParsedParameter> own = new List<ParsedParameter>();
                        ParsedParameter points = new ParsedParameter(loc) { type = "point3", name = "P" };
                        for (int i = 4 * curve; i < 4 * curve + 4; ++i)
                        {
                            points.floats.push_back((Float)x[i]);
                            points.floats.push_back((Float)y[i]);
                            points.floats.push_back((Float)z[i]);
                        }
                        own.Add(points);
                        // (the curve is given in its Bezier values, whatever the basis of the strand)
                        own.Add(Strings(loc, "basis", "bezier"));
                        own.Add(Floats(loc, "width0", Math.Max(0, width[4 * curve])));
                        own.Add(Floats(loc, "width1", Math.Max(0, width[4 * curve + 3])));
                        made = Curve.Create(renderFromObject, objectFromRender, reverseOrientation, new ParameterDictionary(own, given, parameters.ColorSpace()), loc, alloc);
                        for (int i = 0; i < (int)made.size(); ++i)
                        {
                            shapes.push_back(made[i]);
                        }
                    }
                    continue;
                }
                List<ParsedParameter> strandParameters = new List<ParsedParameter>();
                ParsedParameter position = new ParsedParameter(loc) { type = "point3", name = "P" };
                // (the half width: "radius" of the tubes, "width" of the ribbon)
                ParsedParameter half = new ParsedParameter(loc) { type = "float", name = target == "ribbon3" ? "width" : "radius" };
                for (int i = first; i < first + count; ++i)
                {
                    position.floats.push_back(set.points[3 * i]);
                    position.floats.push_back(set.points[3 * i + 1]);
                    position.floats.push_back(set.points[3 * i + 2]);
                    half.floats.push_back((Float)(0.5 * scale * (set.thickness != null ? set.thickness[i] : set.defaultThickness)));
                }
                strandParameters.Add(position);
                strandParameters.Add(half);
                if (useColors)
                {
                    ParsedParameter color = new ParsedParameter(loc) { type = "rgb", name = "Cs" };
                    for (int i = first; i < first + count; ++i)
                    {
                        color.floats.push_back(set.colors != null ? set.colors[3 * i] : set.defaultRed);
                        color.floats.push_back(set.colors != null ? set.colors[3 * i + 1] : set.defaultGreen);
                        color.floats.push_back(set.colors != null ? set.colors[3 * i + 2] : set.defaultBlue);
                    }
                    strandParameters.Add(color);
                }
                ParameterDictionary dictionary = new ParameterDictionary(strandParameters, given, parameters.ColorSpace());
                made = target == "ribbon3" ? Ribbons3.Create(renderFromObject, objectFromRender, reverseOrientation, dictionary, loc)
                                           : Rocaps.Create(renderFromObject, objectFromRender, reverseOrientation, dictionary, loc, target == "rocylinder");
                for (int i = 0; i < (int)made.size(); ++i)
                {
                    shapes.push_back(made[i]);
                }
            }
            if (crossing > 0)
            {
                pbrt.Warning(loc, "hairfile: " + crossing + " of " + set.StrandCount + " strands are not sure to be free of self-intersections (the first, " + firstCrossing + ").");
            }
            return shapes;
        }

        // The parameters of the "trianglemesh" of the strands ("shape" "mesh")
        public static ParameterDictionary Mesh(ParameterDictionary parameters, FileLoc loc)
        {
            string filename = pbrt.ResolveFilename(parameters.GetOneString("filename", ""));
            double scale = parameters.GetOneFloat("thicknessscale", 1);
            string kind = parameters.GetOneString("mesh", "tube");
            if (kind != "tube" && kind != "ribbon")
            {
                pbrt.ErrorExit(loc, "hairfile: \"mesh\" is \"tube\" or \"ribbon\".");
            }
            int sides = kind == "ribbon" ? 2 : Math.Max(3, parameters.GetOneInt("sides", 6));
            string tessellation = parameters.GetOneString("tessellation", "adaptive");
            if (tessellation != "adaptive" && tessellation != "uniform")
            {
                pbrt.ErrorExit(loc, "hairfile: \"tessellation\" is \"adaptive\" or \"uniform\".");
            }
            double angle = parameters.GetOneFloat("angle", 10), error = parameters.GetOneFloat("error", 0.1f);
            int maxDepth = parameters.GetOneInt("maxdepth", 6), steps = Math.Max(1, parameters.GetOneInt("steps", 4));
            bool hasBasis = parameters.GetOneString("basis", "") != "";
            StrandBasis basis = hasBasis ? StrandBasis.Read(parameters, "hairfile", "catmull-rom", 3, loc)
                                         : new StrandBasis("catmull-rom", 3, 1, parameters.GetOneString("wrap", "pinned"), 0.5, null,
                                                           parameters.GetOneFloat("parameterization", 0));
            HairStrandSet set = CyHair.Read(filename);
            List<Float> positions = new List<Float>(), normals = new List<Float>(), uvs = new List<Float>();
            List<int> indices = new List<int>();
            for (int strand = 0; strand < set.StrandCount; ++strand)
            {
                int first = set.first[strand], count = set.first[strand + 1] - first;
                if (count < 2)
                {
                    continue;
                }
                string problem = basis.Check(count);
                if (problem != null)
                {
                    pbrt.ErrorExit(loc, "hairfile: strand " + strand + ": " + problem + ".");
                }
                double[] x = new double[count], y = new double[count], z = new double[count], radius = new double[count];
                for (int i = 0; i < count; ++i)
                {
                    x[i] = set.points[3 * (first + i)];
                    y[i] = set.points[3 * (first + i) + 1];
                    z[i] = set.points[3 * (first + i) + 2];
                    radius[i] = 0.5 * scale * (set.thickness != null ? set.thickness[first + i] : set.defaultThickness);
                }
                StrandBasis strandBasis = basis.Parameterized(x, y, z);
                x = strandBasis.Vectors(x, 3);
                y = strandBasis.Vectors(y, 3);
                z = strandBasis.Vectors(z, 3);
                radius = strandBasis.Curves(radius, 3);
                int curves = x.Length / 4;
                List<SdfVector> centers = new List<SdfVector>(), tangents = new List<SdfVector>();
                List<double> radii = new List<double>(), along = new List<double>();
                for (int curve = 0; curve < curves; ++curve)
                {
                    SdfVector[] p = new SdfVector[4];
                    double[] r = new double[4];
                    for (int i = 0; i < 4; ++i)
                    {
                        p[i] = new SdfVector(x[4 * curve + i], y[4 * curve + i], z[4 * curve + i]);
                        r[i] = radius[4 * curve + i];
                    }
                    List<double> cuts = tessellation == "adaptive" ? HairTessellator.Adaptive(p, r, angle, error, maxDepth) : HairTessellator.Uniform(steps);
                    // (the first ring of a curve is the last of the curve before)
                    for (int i = curve == 0 ? 0 : 1; i < cuts.Count; ++i)
                    {
                        centers.Add(HairTessellator.Point(p, cuts[i]));
                        tangents.Add(HairTessellator.Tangent(p, cuts[i]));
                        radii.Add(Math.Max(0, HairTessellator.Value(r, cuts[i])));
                        along.Add((curve + cuts[i]) / curves);
                    }
                }
                HairTessellator.AddStrand(centers, tangents, radii, along, sides, positions, normals, uvs, indices);
            }
            List<ParsedParameter> mesh = new List<ParsedParameter>();
            ParsedParameter point = new ParsedParameter(loc) { type = "point3", name = "P" };
            ParsedParameter normal = new ParsedParameter(loc) { type = "normal", name = "N" };
            ParsedParameter uv = new ParsedParameter(loc) { type = "point2", name = "uv" };
            ParsedParameter index = new ParsedParameter(loc) { type = "integer", name = "indices" };
            foreach (Float value in positions)
            {
                point.floats.push_back(value);
            }
            foreach (Float value in normals)
            {
                normal.floats.push_back(value);
            }
            foreach (Float value in uvs)
            {
                uv.floats.push_back(value);
            }
            foreach (int value in indices)
            {
                index.ints.push_back(value);
            }
            mesh.Add(point);
            mesh.Add(normal);
            mesh.Add(uv);
            mesh.Add(index);
            return new ParameterDictionary(mesh, parameters.ColorSpace());
        }

        // HairFile Private Methods
        private static ParsedParameter Strings(FileLoc loc, string name, string value)
        {
            ParsedParameter parameter = new ParsedParameter(loc) { type = "string", name = name };
            parameter.strings.push_back(value);
            return parameter;
        }

        private static ParsedParameter Floats(FileLoc loc, string name, double value)
        {
            ParsedParameter parameter = new ParsedParameter(loc) { type = "float", name = name };
            parameter.floats.push_back((Float)value);
            return parameter;
        }
    }
}
