// (not in pbrt-v4) motionblur_exact.cs
//
// The deforming shapes that are exact at every time without being meshes: the quadrics (their numbers have keys), the particles of "points" (centers,
// radii, normals) and the segments of curves (control points, widths, normals). The shape of the time of the ray is made when the ray is
// tested, from the interpolated numbers: the constructors of these shapes are a few numbers and a transformation, so no precomputed table is
// rebuilt. Shapes with a table (the subdivision surfaces, the patches, the nurbs, the dicing, the blobby...) are SlicedShapes.

using System;
using System.Collections.Generic;

namespace pbrt
{
    internal static class ExactMotion
    {
        // The numbers of a quadric in the keys, as Create reads them (with the same defaults)
        private static Float[] QuadricNumbers(string name, ParameterDictionary parameters)
        {
            switch (name)
            {
                case "sphere":
                {
                    Float radius = parameters.GetOneFloat("radius", 1.0f);
                    Float zmin = parameters.GetOneFloat("zmin", -radius);
                    Float zmax = parameters.GetOneFloat("zmax", radius);
                    Float thetamax = parameters.GetOneFloat("thetamax", 360.0f);
                    return new Float[] { radius, zmin, zmax, parameters.GetOneFloat("phimax", thetamax) };
                }
                case "cylinder":
                {
                    Float thetamax = parameters.GetOneFloat("thetamax", 360);
                    return new Float[] { parameters.GetOneFloat("radius", 1), parameters.GetOneFloat("zmin", -1), parameters.GetOneFloat("zmax", 1),
                                         parameters.GetOneFloat("phimax", thetamax) };
                }
                case "disk":
                {
                    Float thetamax = parameters.GetOneFloat("thetamax", 360);
                    return new Float[] { parameters.GetOneFloat("height", 0.0f), parameters.GetOneFloat("radius", 1), parameters.GetOneFloat("innerradius", 0),
                                         parameters.GetOneFloat("phimax", thetamax) };
                }
                case "cone":
                {
                    Float height = parameters.GetOneFloat("height", 1);
                    Float radius = parameters.GetOneFloat("radius", 1);
                    Float zmin = parameters.GetOneFloat("zmin", 0);
                    Float zmax = parameters.GetOneFloat("zmax", height);
                    Float thetamax = parameters.GetOneFloat("thetamax", 360);
                    return new Float[] { height, radius, zmin, zmax, parameters.GetOneFloat("phimax", thetamax) };
                }
                case "paraboloid":
                {
                    Float thetamax = parameters.GetOneFloat("thetamax", 360);
                    return new Float[] { parameters.GetOneFloat("radius", 1), parameters.GetOneFloat("zmin", 0), parameters.GetOneFloat("zmax", 1),
                                         parameters.GetOneFloat("phimax", thetamax) };
                }
                case "hyperboloid":
                {
                    Point3f p1 = parameters.GetOnePoint3f("p1", new Point3f(0, 0, 0));
                    Point3f p2 = parameters.GetOnePoint3f("p2", new Point3f(1, 1, 1));
                    Float thetamax = parameters.GetOneFloat("thetamax", 360);
                    return new Float[] { p1.x, p1.y, p1.z, p2.x, p2.y, p2.z, parameters.GetOneFloat("phimax", thetamax) };
                }
                case "torus":
                {
                    Float minorDefault = parameters.GetOneFloat("minorrad", 0.25f);
                    Float majorDefault = parameters.GetOneFloat("majorrad", 1);
                    return new Float[] { parameters.GetOneFloat("majorradius", majorDefault), parameters.GetOneFloat("minorradius", minorDefault),
                                         parameters.GetOneFloat("phimin", 0), parameters.GetOneFloat("phimax", 360), parameters.GetOneFloat("thetamax", 360) };
                }
            }
            return null;
        }

        private static Shape MakeQuadric(string name, Float[] v, Transform renderFromObject, Transform objectFromRender, bool reverseOrientation)
        {
            switch (name)
            {
                case "sphere":
                    return new Sphere(renderFromObject, objectFromRender, reverseOrientation, v[0], v[1], v[2], v[3]);
                case "cylinder":
                    return new Cylinder(renderFromObject, objectFromRender, reverseOrientation, v[0], v[1], v[2], v[3]);
                case "disk":
                    return new Disk(renderFromObject, objectFromRender, reverseOrientation, v[0], v[1], v[2], v[3]);
                case "cone":
                    return new Cone(renderFromObject, objectFromRender, reverseOrientation, v[0], v[1], v[2], v[3], v[4]);
                case "paraboloid":
                    return new Paraboloid(renderFromObject, objectFromRender, reverseOrientation, v[0], v[1], v[2], v[3]);
                case "hyperboloid":
                    return new Hyperboloid(renderFromObject, objectFromRender, reverseOrientation, new Point3f(v[0], v[1], v[2]), new Point3f(v[3], v[4], v[5]), v[6]);
                default:
                    return new Torus(renderFromObject, objectFromRender, reverseOrientation, v[0], v[1], v[2], v[3], v[4]);
            }
        }

        private static bool SameNumbers(Float[][] values)
        {
            for (int k = 1; k < values.Length; ++k)
            {
                for (int i = 0; i < values[0].Length; ++i)
                {
                    if (values[k][i] != values[0][i])
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private static Float[] Lerp(Float[][] values, Float[] times, Float time)
        {
            int k;
            Float f;
            ShapeMotion.FindKey(times, time, out k, out f);
            Float[] result = new Float[values[0].Length];
            for (int i = 0; i < result.Length; ++i)
            {
                result[i] = (1 - f) * values[k][i] + f * values[k + 1][i];
            }
            return result;
        }

        public static pstd.vector<Shape> TryCreate(string name, Transform renderFromObject, Transform objectFromRender, bool reverseOrientation,
                                                   ParameterDictionary[] parameters, Float[] times, pstd.vector<Shape>[] perKey, FileLoc loc)
        {
            if (QuadricNumbers(name, parameters[0]) != null && perKey[0].size() == 1 && perKey[0][0] is QuadricBase)
            {
                Float[][] values = new Float[times.Length][];
                for (int k = 0; k < times.Length; ++k)
                {
                    values[k] = QuadricNumbers(name, parameters[k]);
                }
                pstd.vector<Shape> single = new pstd.vector<Shape>();
                if (SameNumbers(values))
                {
                    single.push_back(perKey[0][0]);
                    return single;
                }
                single.push_back(new RebuiltShape((Float time) => MakeQuadric(name, Lerp(values, times, time), renderFromObject, objectFromRender, reverseOrientation), times));
                return single;
            }
            if (name == "points")
            {
                return PointsMotion(renderFromObject, objectFromRender, reverseOrientation, parameters, times, perKey, loc);
            }
            if (name == "curves" || name == "curve")
            {
                return CurveMotion(renderFromObject, objectFromRender, reverseOrientation, times, perKey, loc);
            }
            return null;
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // Points

        private sealed class PointKey
        {
            public List<Point3f> points;
            public List<Float> radii;
            public List<Normal3f> normals;
            public List<int> valid = new List<int>();
        }

        private static PointKey ReadPoints(ParameterDictionary parameters)
        {
            PointKey key = new PointKey();
            key.points = parameters.GetPoint3fArray("P");
            key.radii = new List<Float>();
            List<Float> widths = parameters.GetFloatArray("width");
            List<Float> radiusArray = parameters.GetFloatArray("radius");
            if (radiusArray.Count != 0)
            {
                key.radii = radiusArray;
            }
            else if (widths.Count != 0)
            {
                foreach (Float w in widths)
                {
                    key.radii.Add(0.5f * w);
                }
            }
            Float constantRadius = parameters.GetOneFloat("constantradius", 0.5f * parameters.GetOneFloat("constantwidth", 1));
            key.normals = parameters.GetNormal3fArray("N");
            if (key.radii.Count == 0)
            {
                for (int i = 0; i < key.points.Count; ++i)
                {
                    key.radii.Add(constantRadius);
                }
            }
            for (int i = 0; i < key.points.Count; ++i)
            {
                if (key.radii[i] > 0)
                {
                    key.valid.Add(i);
                }
            }
            return key;
        }

        private static pstd.vector<Shape> PointsMotion(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation,
                                                       ParameterDictionary[] parameters, Float[] times, pstd.vector<Shape>[] perKey, FileLoc loc)
        {
            string type = parameters[0].GetOneString("type", "sphere");
            PointKey[] keys = new PointKey[times.Length];
            for (int k = 0; k < times.Length; ++k)
            {
                keys[k] = ReadPoints(parameters[k]);
                if (keys[k].points.Count != keys[0].points.Count || keys[k].valid.Count != keys[0].valid.Count)
                {
                    pbrt.ErrorExit(loc, "points: key " + k + " has another number of points (or of points with a positive size) than the first key.");
                }
                for (int i = 0; i < keys[0].valid.Count; ++i)
                {
                    if (keys[k].valid[i] != keys[0].valid[i])
                    {
                        pbrt.ErrorExit(loc, "points: key " + k + " has other points with a positive size than the first key.");
                    }
                }
            }
            pstd.vector<Shape> shapes = new pstd.vector<Shape>();
            for (int j = 0; j < keys[0].valid.Count; ++j)
            {
                int i = keys[0].valid[j];
                bool moving = false;
                for (int k = 1; k < times.Length; ++k)
                {
                    if (keys[k].points[i] != keys[0].points[i] || keys[k].radii[i] != keys[0].radii[i] ||
                        (type == "disk" && keys[0].normals.Count != 0 && keys[k].normals[i] != keys[0].normals[i]))
                    {
                        moving = true;
                    }
                }
                if (!moving)
                {
                    shapes.push_back(perKey[0][j]);
                    continue;
                }
                int index = i;
                Func<Float, Shape> build = (Float time) =>
                {
                    int k;
                    Float f;
                    ShapeMotion.FindKey(times, time, out k, out f);
                    Point3f c = pbrt.Lerp(f, keys[k].points[index], keys[k + 1].points[index]);
                    Float radius = (1 - f) * keys[k].radii[index] + f * keys[k + 1].radii[index];
                    Transform place = Translate(new Vector3f(c.x, c.y, c.z));
                    if (type == "disk" && keys[0].normals.Count != 0)
                    {
                        Normal3f nn = pbrt.Lerp(f, keys[k].normals[index], keys[k + 1].normals[index]);
                        place = place * RotateFromTo(new Vector3f(0, 0, 1), Normalize(new Vector3f(nn.x, nn.y, nn.z)));
                    }
                    Transform toRender = renderFromObject * place;
                    Transform fromRender = Inverse(toRender);
                    if (type == "sphere")
                    {
                        return new Sphere(toRender, fromRender, reverseOrientation, radius, -radius, radius, 360);
                    }
                    return new Disk(toRender, fromRender, reverseOrientation, 0, radius, 0, 360);
                };
                if (type == "sphere")
                {
                    // The box of a sphere is linear in its center and radius: the union of the boxes of the keys is exact
                    Bounds3f bounds = build(times[0]).Bounds();
                    for (int k = 1; k < times.Length; ++k)
                    {
                        bounds = pbrt.Union(bounds, build(times[k]).Bounds());
                    }
                    shapes.push_back(new RebuiltShape(build, times, bounds, DirectionCone.EntireSphere()));
                }
                else
                {
                    shapes.push_back(new RebuiltShape(build, times));
                }
            }
            return shapes;
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // Curves

        private static pstd.vector<Shape> CurveMotion(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation, Float[] times,
                                                      pstd.vector<Shape>[] perKey, FileLoc loc)
        {
            pstd.vector<Shape> shapes = new pstd.vector<Shape>();
            for (ulong i = 0; i < perKey[0].size(); ++i)
            {
                Curve[] curves = new Curve[times.Length];
                bool moving = false;
                for (int k = 0; k < times.Length; ++k)
                {
                    curves[k] = perKey[k][i] as Curve;
                    if (curves[k] == null || curves[k].UMin() != curves[0].UMin() || curves[k].UMax() != curves[0].UMax())
                    {
                        pbrt.ErrorExit(loc, "curves: key " + k + " of the deforming curves does not have the same curve segments as the first key.");
                    }
                    CurveCommon a = curves[k].Common(), b = curves[0].Common();
                    for (int j = 0; j < 4; ++j)
                    {
                        if (a.cpObj[j] != b.cpObj[j])
                        {
                            moving = true;
                        }
                    }
                    if (a.width[0] != b.width[0] || a.width[1] != b.width[1] || a.n[0] != b.n[0] || a.n[1] != b.n[1])
                    {
                        moving = true;
                    }
                }
                if (!moving)
                {
                    shapes.push_back(curves[0]);
                    continue;
                }
                Curve[] keyCurves = curves;
                CurveCommon first = curves[0].Common();
                bool normals = pbrt.LengthSquared(first.n[0]) > 0;
                Func<Float, Shape> build = (Float time) =>
                {
                    int k;
                    Float f;
                    ShapeMotion.FindKey(times, time, out k, out f);
                    CurveCommon a = keyCurves[k].Common(), b = keyCurves[k + 1].Common();
                    Point3f[] cp = new Point3f[4];
                    for (int j = 0; j < 4; ++j)
                    {
                        cp[j] = pbrt.Lerp(f, a.cpObj[j], b.cpObj[j]);
                    }
                    Normal3f[] n = normals ? new Normal3f[] { pbrt.Lerp(f, a.n[0], b.n[0]), pbrt.Lerp(f, a.n[1], b.n[1]) } : new Normal3f[0];
                    CurveCommon common = new CurveCommon(new pstd.span<Point3f>(cp), (1 - f) * a.width[0] + f * b.width[0], (1 - f) * a.width[1] + f * b.width[1],
                                                         first.type, new pstd.span<Normal3f>(n), renderFromObject, objectFromRender, reverseOrientation);
                    return new Curve(common, keyCurves[0].UMin(), keyCurves[0].UMax());
                };
                // The control points and the widths are linear in the keys, so are the control points of a sub-segment (a blossom): the segment
                // is inside of the union of the boxes of the keys
                Bounds3f bounds = curves[0].Bounds();
                for (int k = 1; k < times.Length; ++k)
                {
                    bounds = pbrt.Union(bounds, curves[k].Bounds());
                }
                shapes.push_back(new RebuiltShape(build, times, bounds, DirectionCone.EntireSphere()));
            }
            return shapes;
        }
    }
}  // namespace pbrt
