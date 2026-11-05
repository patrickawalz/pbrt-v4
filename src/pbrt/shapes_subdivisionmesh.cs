// (not in pbrt-v4) shapes_subdivisionmesh.cs
//
// RiSubdivisionMesh with the tags of RenderMan, on top of the Catmull-Clark limit surface of subdivsurface.cs (exact patches traced by Bezier
// clipping). The RenderMan parameters are converted into those of "catmarksubdiv":
//   scheme     "catmull-clark" (default) or "loop" (forwarded to "loopsubdiv", which has no tags)
//   nvertices  the number of vertices of each face (faceVertexCounts)
//   vertices   the vertex indices of the faces (indices)
//   tags       "interpolateboundary", "hole", "crease", "corner", "smooth", "facevaryinginterpolateboundary"
//   nargs      two integers per tag: the number of integer and of float arguments
//   intargs, floatargs   the arguments of the tags in order
// "interpolateboundary" (no arguments) interpolates the boundary edges and corners; without it the boundary is not interpolated (RenderMan's default,
// the boundary vertices are smoothed as if they had phantom neighbors). "hole" takes the faces to remove. "crease" takes a chain of vertices and one
// sharpness for all its edges. "corner" takes vertices and one sharpness each. A sharpness of 10 or more is infinite. "smooth" and
// "facevaryinginterpolateboundary" are accepted: the first only lets creases and corners of the mesh be smooth (the default here) and the second
// concerns face-varying texture coordinates, which are not used by this shape.

using System;
using System.Collections.Generic;

namespace pbrt
{
    public static class SubdivisionMeshes
    {
        private static ParsedParameter Floats(string type, string name, IEnumerable<float> values)
        {
            ParsedParameter p = new ParsedParameter(null);
            p.type = type;
            p.name = name;
            foreach (float v in values)
            {
                p.floats.push_back(v);
            }
            return p;
        }

        private static ParsedParameter Ints(string name, IEnumerable<int> values)
        {
            ParsedParameter p = new ParsedParameter(null);
            p.type = "integer";
            p.name = name;
            foreach (int v in values)
            {
                p.ints.push_back(v);
            }
            return p;
        }

        private static ParsedParameter Strings(string name, string value)
        {
            ParsedParameter p = new ParsedParameter(null);
            p.type = "string";
            p.name = name;
            p.strings.push_back(value);
            return p;
        }

        public static pstd.vector<Shape> Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation, ParameterDictionary parameters,
                                                FileLoc loc, Allocator alloc)
        {
            pstd.vector<Shape> none = new pstd.vector<Shape>();
            string scheme = parameters.GetOneString("scheme", "catmull-clark");
            List<Point3f> points = parameters.GetPoint3fArray("P");
            List<int> counts = parameters.GetIntArray("nvertices");
            List<int> vertices = parameters.GetIntArray("vertices");
            if (points.Count == 0 || counts.Count == 0 || vertices.Count == 0)
            {
                pbrt.Error(loc, "subdivisionmesh: \"P\", \"nvertices\" and \"vertices\" are required.");
                return none;
            }
            int total = 0;
            foreach (int c in counts)
            {
                total += c;
            }
            if (total != vertices.Count)
            {
                pbrt.Error(loc, "subdivisionmesh: the sum of \"nvertices\" must be the number of \"vertices\".");
                return none;
            }
            foreach (int v in vertices)
            {
                if (v < 0 || v >= points.Count)
                {
                    pbrt.Error(loc, "subdivisionmesh: vertex index out of range.");
                    return none;
                }
            }
            List<float> flat = new List<float>();
            foreach (Point3f p in points)
            {
                flat.Add(p.x);
                flat.Add(p.y);
                flat.Add(p.z);
            }
            List<ParsedParameter> list = new List<ParsedParameter>();
            list.Add(Floats("point3", "P", flat));
            list.Add(Ints("indices", vertices));
            if (parameters.GetIntArray("levels").Count != 0)
            {
                list.Add(Ints("levels", parameters.GetIntArray("levels")));
            }
            if (scheme == "loop")
            {
                ParameterDictionary loopDict = new ParameterDictionary(list, RGBColorSpace.sRGB);
                return Shape.Create("loopsubdiv", renderFromObject, objectFromRender, reverseOrientation, loopDict, new Dictionary<string, FloatTexture>(), loc, alloc);
            }
            if (scheme != "catmull-clark")
            {
                pbrt.Error(loc, "subdivisionmesh: unknown scheme \"" + scheme + "\".");
                return none;
            }
            list.Add(Ints("faceVertexCounts", counts));

            // the tags
            List<string> tags = new List<string>();
            foreach (string tag in StringArray(parameters, "tags"))
            {
                tags.Add(tag);
            }
            List<int> nargs = parameters.GetIntArray("nargs"), intArgs = parameters.GetIntArray("intargs");
            List<Float> floatArgs = parameters.GetFloatArray("floatargs");
            if (nargs.Count != 2 * tags.Count)
            {
                if (tags.Count == 0)
                {
                    nargs = new List<int>();
                }
                else
                {
                    pbrt.Error(loc, "subdivisionmesh: \"nargs\" must hold two integers per tag.");
                    return none;
                }
            }
            string boundary = "none";
            List<int> creaseIndices = new List<int>(), cornerIndices = new List<int>(), holes = new List<int>();
            List<float> creaseWeights = new List<float>(), cornerWeights = new List<float>();
            int intCursor = 0, floatCursor = 0;
            for (int t = 0; t < tags.Count; ++t)
            {
                int ni = nargs[2 * t], nf = nargs[2 * t + 1];
                if (intCursor + ni > intArgs.Count || floatCursor + nf > floatArgs.Count)
                {
                    pbrt.Error(loc, "subdivisionmesh: \"intargs\" or \"floatargs\" is too short for the tags.");
                    return none;
                }
                switch (tags[t])
                {
                    case "interpolateboundary":
                        boundary = "edgeandcorner";
                        break;
                    case "hole":
                        for (int i = 0; i < ni; ++i)
                        {
                            holes.Add(intArgs[intCursor + i]);
                        }
                        break;
                    case "crease":
                        {
                            // a chain of vertices and one sharpness
                            float sharpness = nf > 0 ? Math.Min(10, floatArgs[floatCursor]) : 10;
                            for (int i = 0; i + 1 < ni; ++i)
                            {
                                creaseIndices.Add(intArgs[intCursor + i]);
                                creaseIndices.Add(intArgs[intCursor + i + 1]);
                                creaseWeights.Add(sharpness);
                            }
                            break;
                        }
                    case "corner":
                        for (int i = 0; i < ni; ++i)
                        {
                            cornerIndices.Add(intArgs[intCursor + i]);
                            cornerWeights.Add(nf == ni ? Math.Min(10, floatArgs[floatCursor + i]) : (nf == 1 ? Math.Min(10, floatArgs[floatCursor]) : 10));
                        }
                        break;
                    case "smooth":
                    case "facevaryinginterpolateboundary":
                        break;
                    default:
                        pbrt.Error(loc, "subdivisionmesh: unknown tag \"" + tags[t] + "\" ignored.");
                        break;
                }
                intCursor += ni;
                floatCursor += nf;
            }
            if (creaseWeights.Count != 0)
            {
                list.Add(Ints("creaseindices", creaseIndices));
                list.Add(Floats("float", "creaseweights", creaseWeights));
            }
            if (cornerWeights.Count != 0)
            {
                list.Add(Ints("cornerindices", cornerIndices));
                list.Add(Floats("float", "cornerweights", cornerWeights));
            }
            if (holes.Count != 0)
            {
                list.Add(Ints("holes", holes));
            }
            list.Add(Strings("boundary", boundary));
            ParameterDictionary converted = new ParameterDictionary(list, RGBColorSpace.sRGB);
            return SubdivisionSurface.Create(renderFromObject, objectFromRender, reverseOrientation, converted, loc);
        }

        private static IEnumerable<string> StringArray(ParameterDictionary parameters, string name)
        {
            return parameters.GetStringArray(name);
        }
    }
}  // namespace pbrt
