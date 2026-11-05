// (not in pbrt-v4) shapes_points.cs
//
// RiPoints: particles, as spheres or flat disks. Each particle is a Sphere or a Disk of pbrt (analytic intersection, exact sampling), placed at its
// point. The size is "width" (the diameter, one value per point) or "constantwidth", or in pbrt style "radius" (per point) or "constantradius". A disk
// particle has the normal "N" (one per point), +z without it.

using System;
using System.Collections.Generic;

namespace pbrt
{
    public static class Points
    {
        public static pstd.vector<Shape> Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation, ParameterDictionary parameters,
                                                FileLoc loc, Allocator alloc)
        {
            pstd.vector<Shape> shapes = new pstd.vector<Shape>();
            List<Point3f> points = parameters.GetPoint3fArray("P");
            if (points.Count == 0)
            {
                pbrt.Error(loc, "points: \"P\" must be provided.");
                return shapes;
            }
            string type = parameters.GetOneString("type", "sphere");
            if (type != "sphere" && type != "disk")
            {
                pbrt.Error(loc, "points: \"type\" must be \"sphere\" or \"disk\".");
                return shapes;
            }
            List<Float> radii = new List<Float>();
            List<Float> widths = parameters.GetFloatArray("width");
            List<Float> radiusArray = parameters.GetFloatArray("radius");
            if (radiusArray.Count != 0)
            {
                radii = radiusArray;
            }
            else if (widths.Count != 0)
            {
                foreach (Float w in widths)
                {
                    radii.Add(0.5f * w);
                }
            }
            if (radii.Count != 0 && radii.Count != points.Count)
            {
                pbrt.Error(loc, "points: \"width\" or \"radius\" must hold one value per point.");
                return shapes;
            }
            Float constantRadius = parameters.GetOneFloat("constantradius", 0.5f * parameters.GetOneFloat("constantwidth", 1));
            List<Normal3f> normals = parameters.GetNormal3fArray("N");
            if (normals.Count != 0 && normals.Count != points.Count)
            {
                pbrt.Error(loc, "points: \"N\" must hold one value per point.");
                return shapes;
            }
            for (int i = 0; i < points.Count; ++i)
            {
                Float radius = radii.Count != 0 ? radii[i] : constantRadius;
                if (!(radius > 0))
                {
                    continue;
                }
                Transform place = Translate(new Vector3f(points[i].x, points[i].y, points[i].z));
                if (type == "disk" && normals.Count != 0)
                {
                    Vector3f n = Normalize(new Vector3f(normals[i].x, normals[i].y, normals[i].z));
                    place = place * RotateFromTo(new Vector3f(0, 0, 1), n);
                }
                Transform toRender = renderFromObject * place;
                Transform fromRender = Inverse(toRender);
                if (type == "sphere")
                {
                    shapes.push_back(new Sphere(toRender, fromRender, reverseOrientation, radius, -radius, radius, 360));
                }
                else
                {
                    shapes.push_back(new Disk(toRender, fromRender, reverseOrientation, 0, radius, 0, 360));
                }
            }
            return shapes;
        }
    }
}  // namespace pbrt
