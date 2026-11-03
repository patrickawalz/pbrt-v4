// (not in pbrt-v4) vectordisplacement.cs
//
// Vector displacement: the displacement is a vector V(u, v) in the tangent frame (T, B, N) of the base surface, or in object space,
// not a distance along the normal, so the displaced surface can overhang and curl back over itself (mushrooms, ears, hooks), which a
// height map cannot describe. A vector displacement is a 3 channel map: a floating point image ("string vectormap"), or three
// expressions of u and v ("string functionx", "functiony", "functionz", bounded by interval arithmetic as in shellexpression.cs).
//
// There are two implementations that displace the same surface, to be compared side by side:
//
//  * Tessellated (the way pbrt-v4 displaces a "plymesh"): the mesh is refined until its edges are shorter than "edgelength" and every
//    vertex moves by the vector. Shape "plymesh" takes "string vectormap" instead of "texture displacement".
//
//  * Tessellation free (the shell framework of displacedmesh.cs): Shape "displacedmesh" takes the same parameters. A height map has the
//    nonlinear prism traversal of Ogaki, which needs a height field over the prism, so a vector displacement cannot use it. Here every
//    base triangle is a primitive of the BVH whose box bounds its displaced surface, and the min-max pyramid of the map has a three
//    dimensional box per node: the box of the vectors under the node, carried through the frame (the point of a node is
//    P(a, b) + Vx T(a, b) + Vy B(a, b) + Vz N(a, b), which is multi-affine in (Vx, Vy, Vz, a, b), so the extremes are at the corners of
//    the box of the vectors and of the (a, b) rectangle: 32 evaluations bound a node exactly). Boxes are traversed front to back by
//    the ray; a leaf is a cell of the map whose two micro triangles are clipped to the base triangle, embedded and intersected. The
//    clip makes two triangles that share an edge produce the same polyline on the edge, so the surface is watertight. No geometry is
//    created and the BVH does not change when the map does.
//
// Surface definition (identical in both): the vertices of a mesh get a frame (T, B, N): N the unit vertex normal; T the sum over the
// triangles around the vertex, weighted by the angle at the vertex, of dP/du (from the uv of the triangle), made orthogonal to N;
// B = s N x T, where s = +-1 follows the orientation of the uv map. A point of the base triangle has the frame that is the linear
// interpolation of the frames of its vertices (not renormalized, which keeps the bounds exact). The displaced point is
// P + Vx T + Vy B + Vz N, in object space. With "string vectorspace" "object" the frame is the identity: V is an object space vector.
// The vector of an image is vectorscale * (rgb - vectorbias): 0.5 is the usual bias for an 8 bit map.

using System;
using System.Collections.Generic;

namespace pbrt
{

    // VectorFrames Definition: the frame of every vertex of a mesh
    public static unsafe class VectorFrames
    {
        // The unit tangent T and the handedness s (+1 or -1) of every vertex; n must be unit length. The frame is (T, s N x T, N).
        public static void Compute(Point3f[] p, Normal3f[] n, Point2f[] uv, int[] indices, int vertexCount, out SdfVector[] tangents,
                                   out double[] handedness)
        {
            SdfVector[] sum = new SdfVector[vertexCount];
            double[] sign = new double[vertexCount];
            for (int t = 0; t < indices.Length / 3; ++t)
            {
                int[] v = new int[] { indices[3 * t], indices[3 * t + 1], indices[3 * t + 2] };
                SdfVector[] q = new SdfVector[3];
                for (int k = 0; k < 3; ++k)
                {
                    q[k] = new SdfVector(p[v[k]].x, p[v[k]].y, p[v[k]].z);
                }
                SdfVector e1 = q[1] - q[0], e2 = q[2] - q[0];
                double du1 = uv[v[1]].x - uv[v[0]].x, dv1 = uv[v[1]].y - uv[v[0]].y;
                double du2 = uv[v[2]].x - uv[v[0]].x, dv2 = uv[v[2]].y - uv[v[0]].y;
                double determinant = du1 * dv2 - du2 * dv1;
                if (Math.Abs(determinant) < 1e-300)
                {
                    continue;
                }
                SdfVector dpdu = (e1 * dv2 - e2 * dv1) * (1 / determinant);
                SdfVector dpdv = (e2 * du1 - e1 * du2) * (1 / determinant);
                SdfVector faceCross = SdfVector.Cross(dpdu, dpdv);
                if (dpdu.LengthSquared() == 0)
                {
                    continue;
                }
                SdfVector unitU = dpdu.Normalized();
                for (int k = 0; k < 3; ++k)
                {
                    SdfVector a = (q[(k + 1) % 3] - q[k]).Normalized(), b = (q[(k + 2) % 3] - q[k]).Normalized();
                    double angle = Math.Acos(Math.Max(-1, Math.Min(1, SdfVector.Dot(a, b))));
                    sum[v[k]] = sum[v[k]] + unitU * angle;
                    SdfVector unitNormal = new SdfVector(n[v[k]].x, n[v[k]].y, n[v[k]].z);
                    sign[v[k]] += angle * (SdfVector.Dot(faceCross, unitNormal) >= 0 ? 1 : -1);
                }
            }
            tangents = new SdfVector[vertexCount];
            handedness = new double[vertexCount];
            for (int i = 0; i < vertexCount; ++i)
            {
                SdfVector normal = new SdfVector(n[i].x, n[i].y, n[i].z);
                SdfVector tangent = sum[i] - normal * SdfVector.Dot(sum[i], normal);
                if (tangent.LengthSquared() < 1e-24)
                {
                    Vector3f s, t;
                    Vector3f nf = new Vector3f((Float)normal.x, (Float)normal.y, (Float)normal.z);
                    pbrt.CoordinateSystem(nf, &s, &t);
                    tangent = new SdfVector(s.x, s.y, s.z);
                }
                tangents[i] = tangent.Normalized();
                handedness[i] = sign[i] >= 0 ? 1 : -1;
            }
        }
    }

    // VectorMapImage Definition: a 3 channel floating point image sampled by the texels, as the grid of a map
    public sealed class VectorMapImage
    {
        public VectorMapImage(string filename, double scale, double bias)
        {
            ImageAndMetadata loaded = Image.Read(filename, null, ColorEncoding.Linear);
            Image image = loaded.image;
            if (image.NChannels() < 3)
            {
                pbrt.ErrorExit("vector displacement map \"" + filename + "\" must have three channels.");
            }
            width = image.Resolution().x;
            height = image.Resolution().y;
            values = new float[3 * width * height];
            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    for (int c = 0; c < 3; ++c)
                    {
                        // Row 0 of the grid is v = 0, which is the last row of the image (as for the image textures of pbrt)
                        values[3 * (y * width + x) + c] = (float)(scale * (image.GetChannel(new Point2i(x, height - 1 - y), c) - bias));
                    }
                }
            }
        }

        // A map from vectors given directly (row 0 is v = 0)
        public VectorMapImage(int width, int height, float[] values)
        {
            this.width = width;
            this.height = height;
            this.values = values;
        }

        // The vector at (u, v) by bilinear interpolation of the texels; at the texel centers it is exactly the value of the texel
        public void Bilinear(double u, double v, out double x, out double y, out double z)
        {
            double fx = Math.Max(0, Math.Min(1, u)) * (width - 1), fy = Math.Max(0, Math.Min(1, v)) * (height - 1);
            int ix = Math.Min((int)fx, Math.Max(0, width - 2)), iy = Math.Min((int)fy, Math.Max(0, height - 2));
            double tx = fx - ix, ty = fy - iy;
            int ix1 = Math.Min(ix + 1, width - 1), iy1 = Math.Min(iy + 1, height - 1);
            double[] result = new double[3];
            for (int c = 0; c < 3; ++c)
            {
                double a = values[3 * (iy * width + ix) + c], b = values[3 * (iy * width + ix1) + c];
                double d = values[3 * (iy1 * width + ix) + c], e = values[3 * (iy1 * width + ix1) + c];
                result[c] = (1 - ty) * ((1 - tx) * a + tx * b) + ty * ((1 - tx) * d + tx * e);
            }
            x = result[0];
            y = result[1];
            z = result[2];
        }

        public readonly int width, height;
        public readonly float[] values;
    }

    // ShellVectorField Definition: a vector displacement on a grid of cellsX x cellsY cells, with the box of the vectors under every node
    // of a quadtree (level 0: a cell, level k: 2^k x 2^k cells)
    public abstract class ShellVectorField
    {
        protected ShellVectorField(int cellsX, int cellsY)
        {
            this.cellsX = cellsX;
            this.cellsY = cellsY;
            int levelCount = 1;
            int sizeX = cellsX, sizeY = cellsY;
            while (sizeX > 1 || sizeY > 1)
            {
                sizeX = (sizeX + 1) / 2;
                sizeY = (sizeY + 1) / 2;
                ++levelCount;
            }
            levels = levelCount;
        }

        // The vector at a grid point
        public abstract void Sample(int x, int y, out double vx, out double vy, out double vz);

        // The box of the vectors of the grid points under a node: box[0..2] the lowest and box[3..5] the highest components
        public abstract void NodeBox(int level, int i, int j, double[] box);

        public int LevelWidth(int level)
        {
            return ((cellsX - 1) >> level) + 1;
        }

        public int LevelHeight(int level)
        {
            return ((cellsY - 1) >> level) + 1;
        }

        public readonly int cellsX, cellsY, levels;
    }

    // ShellVectorGrid Definition: the vectors of an image, with a min-max pyramid of boxes
    public sealed class ShellVectorGrid : ShellVectorField
    {
        public ShellVectorGrid(VectorMapImage image)
            : base(image.width - 1, image.height - 1)
        {
            this.image = image;
            pyramid = new float[levels][];
            for (int level = 1; level < levels; ++level)
            {
                int nodesX = LevelWidth(level), nodesY = LevelHeight(level);
                float[] boxes = new float[6 * nodesX * nodesY];
                int size = 1 << level;
                for (int j = 0; j < nodesY; ++j)
                {
                    for (int i = 0; i < nodesX; ++i)
                    {
                        int x0 = i * size, x1 = Math.Min((i + 1) * size, cellsX), y0 = j * size, y1 = Math.Min((j + 1) * size, cellsY);
                        float[] box = new float[6];
                        for (int c = 0; c < 3; ++c)
                        {
                            box[c] = float.MaxValue;
                            box[3 + c] = float.MinValue;
                        }
                        if (level == 1)
                        {
                            for (int y = y0; y <= y1; ++y)
                            {
                                for (int x = x0; x <= x1; ++x)
                                {
                                    for (int c = 0; c < 3; ++c)
                                    {
                                        float value = image.values[3 * (y * image.width + x) + c];
                                        box[c] = Math.Min(box[c], value);
                                        box[3 + c] = Math.Max(box[3 + c], value);
                                    }
                                }
                            }
                        }
                        else
                        {
                            // The union of the children (which share the vertices on their common boundary)
                            for (int cj = 2 * j; cj <= 2 * j + 1; ++cj)
                            {
                                for (int ci = 2 * i; ci <= 2 * i + 1; ++ci)
                                {
                                    if (ci < LevelWidth(level - 1) && cj < LevelHeight(level - 1))
                                    {
                                        int child = 6 * (cj * LevelWidth(level - 1) + ci);
                                        for (int c = 0; c < 3; ++c)
                                        {
                                            box[c] = Math.Min(box[c], pyramid[level - 1][child + c]);
                                            box[3 + c] = Math.Max(box[3 + c], pyramid[level - 1][child + 3 + c]);
                                        }
                                    }
                                }
                            }
                        }
                        Array.Copy(box, 0, boxes, 6 * (j * nodesX + i), 6);
                    }
                }
                pyramid[level] = boxes;
            }
        }

        public override void Sample(int x, int y, out double vx, out double vy, out double vz)
        {
            int offset = 3 * (y * image.width + x);
            vx = image.values[offset];
            vy = image.values[offset + 1];
            vz = image.values[offset + 2];
        }

        public override void NodeBox(int level, int i, int j, double[] box)
        {
            if (level == 0)
            {
                for (int c = 0; c < 3; ++c)
                {
                    box[c] = double.MaxValue;
                    box[3 + c] = double.MinValue;
                }
                for (int y = j; y <= j + 1; ++y)
                {
                    for (int x = i; x <= i + 1; ++x)
                    {
                        int offset = 3 * (y * image.width + x);
                        for (int c = 0; c < 3; ++c)
                        {
                            box[c] = Math.Min(box[c], image.values[offset + c]);
                            box[3 + c] = Math.Max(box[3 + c], image.values[offset + c]);
                        }
                    }
                }
                return;
            }
            int index = 6 * (j * LevelWidth(level) + i);
            for (int c = 0; c < 6; ++c)
            {
                box[c] = pyramid[level][index + c];
            }
        }

        public int ByteCount()
        {
            long total = 4L * image.values.Length;
            for (int level = 1; level < levels; ++level)
            {
                total += 4L * pyramid[level].Length;
            }
            return (int)Math.Min(total, int.MaxValue);
        }

        private readonly VectorMapImage image;
        private readonly float[][] pyramid;
    }

    // ShellVectorProcedural Definition: three expressions of (u, v), bounded with interval arithmetic
    public sealed class ShellVectorProcedural : ShellVectorField
    {
        public ShellVectorProcedural(ShellExpression x, ShellExpression y, ShellExpression z, int depth, double scale)
            : base(1 << depth, 1 << depth)
        {
            expressions = new ShellExpression[] { x, y, z };
            this.scale = scale;
        }

        public override void Sample(int x, int y, out double vx, out double vy, out double vz)
        {
            double u = (double)x / cellsX, v = (double)y / cellsY;
            vx = scale * expressions[0].Evaluate(u, v);
            vy = scale * expressions[1].Evaluate(u, v);
            vz = scale * expressions[2].Evaluate(u, v);
        }

        public override void NodeBox(int level, int i, int j, double[] box)
        {
            int size = 1 << level;
            double u0 = (double)(i * size) / cellsX, u1 = (double)Math.Min((i + 1) * size, cellsX) / cellsX;
            double v0 = (double)(j * size) / cellsY, v1 = (double)Math.Min((j + 1) * size, cellsY) / cellsY;
            for (int c = 0; c < 3; ++c)
            {
                expressions[c].Bounds(u0, u1, v0, v1, out double low, out double high);
                double a = scale * low, b = scale * high;
                box[c] = Math.Min(a, b);
                box[3 + c] = Math.Max(a, b);
            }
        }

        private readonly ShellExpression[] expressions;
        private readonly double scale;
    }

    // VectorDisplacedMesh Definition: the base triangles of a mesh, in object space, with their frames and the field
    public sealed class VectorDisplacedMesh
    {
        public VectorDisplacedMesh(Transform renderFromObject, bool reverseOrientation, int[] indices, Point3f[] p, Normal3f[] n,
                                   Point2f[] uv, ShellVectorField field, bool tangentSpace, bool smoothNormals)
        {
            this.renderFromObject = renderFromObject;
            this.objectFromRender = pbrt.Inverse(renderFromObject);
            this.indices = indices;
            this.uv = uv;
            this.field = field;
            this.reverseOrientation = reverseOrientation;
            this.smoothNormals = smoothNormals;
            int vertexCount = p.Length;
            objectP = new SdfVector[vertexCount];
            frameT = new SdfVector[vertexCount];
            frameB = new SdfVector[vertexCount];
            frameN = new SdfVector[vertexCount];
            Normal3f[] unit = new Normal3f[vertexCount];
            for (int i = 0; i < vertexCount; ++i)
            {
                unit[i] = pbrt.Normalize(n[i]);
                objectP[i] = new SdfVector(p[i].x, p[i].y, p[i].z);
            }
            if (tangentSpace)
            {
                VectorFrames.Compute(p, unit, uv, indices, vertexCount, out SdfVector[] tangents, out double[] handedness);
                for (int i = 0; i < vertexCount; ++i)
                {
                    frameN[i] = new SdfVector(unit[i].x, unit[i].y, unit[i].z);
                    frameT[i] = tangents[i];
                    frameB[i] = SdfVector.Cross(frameN[i], frameT[i]) * handedness[i];
                }
            }
            else
            {
                for (int i = 0; i < vertexCount; ++i)
                {
                    frameT[i] = new SdfVector(1, 0, 0);
                    frameB[i] = new SdfVector(0, 1, 0);
                    frameN[i] = new SdfVector(0, 0, 1);
                }
            }
            int triangleCount = indices.Length / 3;
            gridCorners = new double[6 * triangleCount];
            tileU = new double[triangleCount];
            tileV = new double[triangleCount];
            normalSign = new double[triangleCount];
            for (int t = 0; t < triangleCount; ++t)
            {
                Point2f t0 = uv[indices[3 * t]], t1 = uv[indices[3 * t + 1]], t2 = uv[indices[3 * t + 2]];
                tileU[t] = Math.Floor(((double)t0.x + t1.x + t2.x) / 3);
                tileV[t] = Math.Floor(((double)t0.y + t1.y + t2.y) / 3);
                gridCorners[6 * t] = (t0.x - tileU[t]) * field.cellsX;
                gridCorners[6 * t + 1] = (t0.y - tileV[t]) * field.cellsY;
                gridCorners[6 * t + 2] = (t1.x - tileU[t]) * field.cellsX;
                gridCorners[6 * t + 3] = (t1.y - tileV[t]) * field.cellsY;
                gridCorners[6 * t + 4] = (t2.x - tileU[t]) * field.cellsX;
                gridCorners[6 * t + 5] = (t2.y - tileV[t]) * field.cellsY;
                // The sign that makes the cross product of the tangents of the grid point to the side of the base normal
                double determinant = (gridCorners[6 * t + 2] - gridCorners[6 * t]) * (gridCorners[6 * t + 5] - gridCorners[6 * t + 1]) -
                                     (gridCorners[6 * t + 4] - gridCorners[6 * t]) * (gridCorners[6 * t + 3] - gridCorners[6 * t + 1]);
                SdfVector[] q = new SdfVector[3];
                SdfVector sumN = new SdfVector(0, 0, 0);
                for (int k = 0; k < 3; ++k)
                {
                    q[k] = objectP[indices[3 * t + k]];
                    sumN = sumN + frameN[indices[3 * t + k]];
                }
                double side = SdfVector.Dot(SdfVector.Cross(q[1] - q[0], q[2] - q[0]), sumN) >= 0 ? 1 : -1;
                // dX/dx x dX/dy = det(d(a, b)/d(x, y)) (P1 - P0) x (P2 - P0) for V = 0, and det(d(a, b)/d(x, y)) = 1 / determinant
                normalSign[t] = (determinant >= 0 ? 1 : -1) * side;
            }
        }

        public int TriangleCount()
        {
            return indices.Length / 3;
        }

        public readonly Transform renderFromObject, objectFromRender;
        public readonly int[] indices;
        public readonly Point2f[] uv;
        public readonly ShellVectorField field;
        public readonly bool reverseOrientation, smoothNormals;
        public readonly SdfVector[] objectP, frameT, frameB, frameN;
        public readonly double[] gridCorners, tileU, tileV, normalSign;
    }

    // VectorDisplacedTriangle Definition: one base triangle of a vector displaced mesh
    public sealed unsafe class VectorDisplacedTriangle : Shape
    {
        public VectorDisplacedTriangle(VectorDisplacedMesh mesh, int triIndex)
        {
            this.mesh = mesh;
            this.triIndex = triIndex;
            bounds = ComputeBounds();
        }

        public override Bounds3f Bounds()
        {
            return bounds;
        }

        public override DirectionCone NormalBounds()
        {
            return DirectionCone.EntireSphere();
        }

        public override pstd.optional<ShapeIntersection> Intersect(Ray ray, Float tMax = Infinity)
        {
            if (!Trace(ray, tMax, false, out VectorHit hit))
            {
                return default;
            }
            SurfaceInteraction intr = InteractionFromHit(ray, hit);
            return new ShapeIntersection(intr, (Float)hit.t);
        }

        public override bool IntersectP(Ray ray, Float tMax = Infinity)
        {
            return Trace(ray, tMax, true, out VectorHit hit);
        }

        public override Float Area()
        {
            int i0 = mesh.indices[3 * triIndex], i1 = mesh.indices[3 * triIndex + 1], i2 = mesh.indices[3 * triIndex + 2];
            Point3f a = mesh.renderFromObject.Apply(new Point3f((Float)mesh.objectP[i0].x, (Float)mesh.objectP[i0].y, (Float)mesh.objectP[i0].z));
            Point3f b = mesh.renderFromObject.Apply(new Point3f((Float)mesh.objectP[i1].x, (Float)mesh.objectP[i1].y, (Float)mesh.objectP[i1].z));
            Point3f c = mesh.renderFromObject.Apply(new Point3f((Float)mesh.objectP[i2].x, (Float)mesh.objectP[i2].y, (Float)mesh.objectP[i2].z));
            return (Float)(0.5 * pbrt.Length(pbrt.Cross(b - a, c - a)));
        }

        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            pbrt.ErrorExit("displacedmesh: area lights on a displaced mesh are not supported yet.");
            return default;
        }

        public override Float PDF(Interaction intr)
        {
            pbrt.ErrorExit("displacedmesh: area lights on a displaced mesh are not supported yet.");
            return 0;
        }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            pbrt.ErrorExit("displacedmesh: area lights on a displaced mesh are not supported yet.");
            return default;
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            pbrt.ErrorExit("displacedmesh: area lights on a displaced mesh are not supported yet.");
            return 0;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ VectorDisplacedTriangle triIndex: %d ]", triIndex);
        }

        // What the traversal did on the calling thread since the last reset
        public static ShellStatistics ThreadStatistics()
        {
            return Workspace().statistics;
        }

        public static void ResetThreadStatistics()
        {
            Workspace().statistics = new ShellStatistics();
        }

        // Creates the triangles of the mesh from the parameters of a "displacedmesh" shape that has a vector map or functions
        public static pstd.vector<Shape> Create(Transform renderFromObject, bool reverseOrientation, ParameterDictionary parameters,
                                                FileLoc loc)
        {
            DisplacedTriangle.MeshData(parameters.GetOneString("filename", ""), parameters.GetIntArray("indices"),
                                       parameters.GetPoint3fArray("P"), parameters.GetNormal3fArray("N"), parameters.GetPoint2fArray("uv"), loc,
                                       out int[] indices, out Point3f[] p, out Normal3f[] n, out Point2f[] uvArray);
            double scale = parameters.GetOneFloat("vectorscale", 1);
            string space = parameters.GetOneString("vectorspace", "tangent");
            if (space != "tangent" && space != "object")
            {
                pbrt.ErrorExit(loc, "displacedmesh: \"vectorspace\" must be \"tangent\" or \"object\".");
            }
            string mapName = parameters.GetOneString("vectormap", "");
            ShellVectorField field;
            if (mapName.Length != 0)
            {
                VectorMapImage image = new VectorMapImage(pbrt.ResolveFilename(mapName), scale, parameters.GetOneFloat("vectorbias", 0));
                field = new ShellVectorGrid(image);
            }
            else
            {
                int resolution = Math.Max(2, parameters.GetOneInt("resolution", 1024));
                int depth = 1;
                while ((1 << depth) < resolution - 1 && depth < 24)
                {
                    ++depth;
                }
                try
                {
                    field = new ShellVectorProcedural(ShellExpression.Parse(parameters.GetOneString("functionx", "0")),
                                                      ShellExpression.Parse(parameters.GetOneString("functiony", "0")),
                                                      ShellExpression.Parse(parameters.GetOneString("functionz", "0")), depth, scale);
                }
                catch (FormatException e)
                {
                    pbrt.ErrorExit(loc, "displacedmesh: \"string functionx/y/z\": " + e.Message);
                    return null;
                }
            }
            VectorDisplacedMesh mesh = new VectorDisplacedMesh(renderFromObject, reverseOrientation, indices, p, n, uvArray, field,
                                                               space == "tangent", parameters.GetOneBool("smoothnormals", true));
            pstd.vector<Shape> shapes = new pstd.vector<Shape>();
            for (int i = 0; i < mesh.TriangleCount(); ++i)
            {
                shapes.push_back(new VectorDisplacedTriangle(mesh, i));
            }
            return shapes;
        }

        // The result of a trace: the closest hit
        private struct VectorHit
        {
            public double t;
            // The three vertices of the triangle that was hit, embedded in object space, and their grid coordinates
            public SdfVector x0, x1, x2;
            public double gx0, gy0, gx1, gy1, gx2, gy2;
            public double hitX, hitY;
            public int cellX, cellY;
        }

        [ThreadStatic]
        private static VectorWorkspace workspace;

        private static VectorWorkspace Workspace()
        {
            if (workspace == null)
            {
                workspace = new VectorWorkspace();
            }
            return workspace;
        }

        // The scratch memory of a thread
        private sealed class VectorWorkspace
        {
            public readonly int[] stackLevel = new int[512], stackI = new int[512], stackJ = new int[512];
            public readonly double[] stackT = new double[512];
            public readonly double[] box = new double[6], cellVectors = new double[12];
            public readonly SdfVector[] embedded = new SdfVector[8];
            public readonly int[] childLevel = new int[4], childI = new int[4], childJ = new int[4];
            public readonly double[] childT = new double[4];
            public ShellStatistics statistics = new ShellStatistics();
            // Polygons of a clipped micro triangle: 8 vertices of (x, y, a, b, vx, vy, vz)
            public readonly double[] polygonA = new double[8 * 7], polygonB = new double[8 * 7];
        }

        // The point of the displaced surface at (a, b) with the vector (vx, vy, vz), in object space
        private SdfVector Embed(double a, double b, double vx, double vy, double vz)
        {
            int i0 = mesh.indices[3 * triIndex], i1 = mesh.indices[3 * triIndex + 1], i2 = mesh.indices[3 * triIndex + 2];
            double c = 1 - a - b;
            SdfVector point = mesh.objectP[i0] * c + mesh.objectP[i1] * a + mesh.objectP[i2] * b;
            SdfVector frameT = mesh.frameT[i0] * c + mesh.frameT[i1] * a + mesh.frameT[i2] * b;
            SdfVector frameB = mesh.frameB[i0] * c + mesh.frameB[i1] * a + mesh.frameB[i2] * b;
            SdfVector frameN = mesh.frameN[i0] * c + mesh.frameN[i1] * a + mesh.frameN[i2] * b;
            return point + frameT * vx + frameB * vy + frameN * vz;
        }

        // (a, b) of a grid point
        private void GridToBarycentric(double x, double y, out double a, out double b)
        {
            double[] g = mesh.gridCorners;
            int k = 6 * triIndex;
            double ex1 = g[k + 2] - g[k], ey1 = g[k + 3] - g[k + 1], ex2 = g[k + 4] - g[k], ey2 = g[k + 5] - g[k + 1];
            double determinant = ex1 * ey2 - ex2 * ey1;
            double dx = x - g[k], dy = y - g[k + 1];
            a = (dx * ey2 - dy * ex2) / determinant;
            b = (ex1 * dy - ey1 * dx) / determinant;
        }

        // The box of the displaced surface of the triangle in render space
        private Bounds3f ComputeBounds()
        {
            ShellVectorField field = mesh.field;
            double[] boxLow = new double[3], boxHigh = new double[3];
            for (int c = 0; c < 3; ++c)
            {
                boxLow[c] = double.MaxValue;
                boxHigh[c] = double.MinValue;
            }
            double[] nodeBox = new double[6];
            // The union of the vectors of the nodes (down to 16 x 16 cells) that touch the triangle
            int rootLevel = field.levels - 1;
            Stack<(int, int, int)> stack = new Stack<(int, int, int)>();
            stack.Push((rootLevel, 0, 0));
            while (stack.Count > 0)
            {
                (int level, int i, int j) = stack.Pop();
                int size = 1 << level;
                if (!RectangleTouchesTriangle(i * size, j * size, (i + 1) * size, (j + 1) * size))
                {
                    continue;
                }
                if (level > 4)
                {
                    for (int cj = 2 * j; cj <= 2 * j + 1; ++cj)
                    {
                        for (int ci = 2 * i; ci <= 2 * i + 1; ++ci)
                        {
                            if (ci < field.LevelWidth(level - 1) && cj < field.LevelHeight(level - 1))
                            {
                                stack.Push((level - 1, ci, cj));
                            }
                        }
                    }
                    continue;
                }
                field.NodeBox(level, i, j, nodeBox);
                for (int c = 0; c < 3; ++c)
                {
                    boxLow[c] = Math.Min(boxLow[c], nodeBox[c]);
                    boxHigh[c] = Math.Max(boxHigh[c], nodeBox[3 + c]);
                }
            }
            Bounds3f result = new Bounds3f();
            if (boxLow[0] > boxHigh[0])
            {
                // The triangle touches no cell: its base triangle
                boxLow = new double[] { 0, 0, 0 };
                boxHigh = new double[] { 0, 0, 0 };
            }
            bool first = true;
            for (int corner = 0; corner < 3; ++corner)
            {
                double a = corner == 1 ? 1 : 0, b = corner == 2 ? 1 : 0;
                for (int mask = 0; mask < 8; ++mask)
                {
                    SdfVector q = Embed(a, b, (mask & 1) != 0 ? boxHigh[0] : boxLow[0], (mask & 2) != 0 ? boxHigh[1] : boxLow[1],
                                        (mask & 4) != 0 ? boxHigh[2] : boxLow[2]);
                    Point3f point = mesh.renderFromObject.Apply(new Point3f((Float)q.x, (Float)q.y, (Float)q.z));
                    result = first ? new Bounds3f(point) : pbrt.Union(result, point);
                    first = false;
                }
            }
            float extent = Math.Max(Math.Max(Math.Abs(result.pMin.x), Math.Abs(result.pMax.x)),
                                    Math.Max(Math.Max(Math.Abs(result.pMin.y), Math.Abs(result.pMax.y)),
                                             Math.Max(Math.Abs(result.pMin.z), Math.Abs(result.pMax.z))));
            float amount = 1e-5f * Math.Max(1f, extent);
            Vector3f pad = new Vector3f(amount, amount, amount);
            return new Bounds3f(result.pMin - pad, result.pMax + pad);
        }

        // Does the rectangle [x0, x1] x [y0, y1] (grid units) touch the uv triangle of this base triangle?
        private bool RectangleTouchesTriangle(double x0, double y0, double x1, double y1)
        {
            double[] full = mesh.gridCorners;
            double[] g = new double[6];
            Array.Copy(full, 6 * triIndex, g, 0, 6);
            double minX = Math.Min(g[0], Math.Min(g[2], g[4])), maxX = Math.Max(g[0], Math.Max(g[2], g[4]));
            double minY = Math.Min(g[1], Math.Min(g[3], g[5])), maxY = Math.Max(g[1], Math.Max(g[3], g[5]));
            if (maxX < x0 || minX > x1 || maxY < y0 || minY > y1)
            {
                return false;
            }
            // Separating axes of the edges of the triangle
            for (int e = 0; e < 3; ++e)
            {
                double ax = g[2 * e], ay = g[2 * e + 1], bx = g[2 * ((e + 1) % 3)], by = g[2 * ((e + 1) % 3) + 1];
                double cx = g[2 * ((e + 2) % 3)], cy = g[2 * ((e + 2) % 3) + 1];
                double nx = by - ay, ny = ax - bx;
                if (nx * (cx - ax) + ny * (cy - ay) < 0)
                {
                    nx = -nx;
                    ny = -ny;
                }
                // The triangle is on the positive side; the rectangle is outside when all of its corners are on the negative side
                double best = Math.Max(Math.Max(nx * (x0 - ax) + ny * (y0 - ay), nx * (x1 - ax) + ny * (y0 - ay)),
                                       Math.Max(nx * (x0 - ax) + ny * (y1 - ay), nx * (x1 - ax) + ny * (y1 - ay)));
                if (best < 0)
                {
                    return false;
                }
            }
            return true;
        }

        // The box of the displaced surface under a node, in object space: the multi-affine extremes over the corners of the box of the
        // vectors and of the rectangle of (a, b) of the node (clamped to the triangle)
        private void NodeSpace(VectorWorkspace ws, int level, int i, int j, out SdfVector low, out SdfVector high)
        {
            ShellVectorField field = mesh.field;
            field.NodeBox(level, i, j, ws.box);
            int size = 1 << level;
            double x0 = i * size, x1 = Math.Min((i + 1) * size, field.cellsX), y0 = j * size, y1 = Math.Min((j + 1) * size, field.cellsY);
            double a0 = double.MaxValue, a1 = double.MinValue, b0 = double.MaxValue, b1 = double.MinValue;
            for (int corner = 0; corner < 4; ++corner)
            {
                GridToBarycentric((corner & 1) != 0 ? x1 : x0, (corner & 2) != 0 ? y1 : y0, out double a, out double b);
                a0 = Math.Min(a0, a);
                a1 = Math.Max(a1, a);
                b0 = Math.Min(b0, b);
                b1 = Math.Max(b1, b);
            }
            a0 = Math.Max(0, a0);
            b0 = Math.Max(0, b0);
            a1 = Math.Min(1, a1);
            b1 = Math.Min(1, b1);
            double lx = double.MaxValue, ly = double.MaxValue, lz = double.MaxValue;
            double hx = double.MinValue, hy = double.MinValue, hz = double.MinValue;
            int i0 = mesh.indices[3 * triIndex], i1 = mesh.indices[3 * triIndex + 1], i2 = mesh.indices[3 * triIndex + 2];
            for (int corner = 0; corner < 4; ++corner)
            {
                double a = (corner & 1) != 0 ? a1 : a0, b = (corner & 2) != 0 ? b1 : b0;
                double c = 1 - a - b;
                SdfVector point = mesh.objectP[i0] * c + mesh.objectP[i1] * a + mesh.objectP[i2] * b;
                SdfVector frameT = mesh.frameT[i0] * c + mesh.frameT[i1] * a + mesh.frameT[i2] * b;
                SdfVector frameB = mesh.frameB[i0] * c + mesh.frameB[i1] * a + mesh.frameB[i2] * b;
                SdfVector frameN = mesh.frameN[i0] * c + mesh.frameN[i1] * a + mesh.frameN[i2] * b;
                // For a fixed (a, b) the point is affine in the vector, so every component is extreme at the ends of the box of the
                // vectors: P + sum over the three frame axes of the smaller and the larger of axis * low and axis * high
                double[] box = ws.box;
                double tx0 = frameT.x * box[0], tx1 = frameT.x * box[3], ty0 = frameT.y * box[0], ty1 = frameT.y * box[3];
                double tz0 = frameT.z * box[0], tz1 = frameT.z * box[3];
                double bx0 = frameB.x * box[1], bx1 = frameB.x * box[4], by0 = frameB.y * box[1], by1 = frameB.y * box[4];
                double bz0 = frameB.z * box[1], bz1 = frameB.z * box[4];
                double nx0 = frameN.x * box[2], nx1 = frameN.x * box[5], ny0 = frameN.y * box[2], ny1 = frameN.y * box[5];
                double nz0 = frameN.z * box[2], nz1 = frameN.z * box[5];
                lx = Math.Min(lx, point.x + Math.Min(tx0, tx1) + Math.Min(bx0, bx1) + Math.Min(nx0, nx1));
                hx = Math.Max(hx, point.x + Math.Max(tx0, tx1) + Math.Max(bx0, bx1) + Math.Max(nx0, nx1));
                ly = Math.Min(ly, point.y + Math.Min(ty0, ty1) + Math.Min(by0, by1) + Math.Min(ny0, ny1));
                hy = Math.Max(hy, point.y + Math.Max(ty0, ty1) + Math.Max(by0, by1) + Math.Max(ny0, ny1));
                lz = Math.Min(lz, point.z + Math.Min(tz0, tz1) + Math.Min(bz0, bz1) + Math.Min(nz0, nz1));
                hz = Math.Max(hz, point.z + Math.Max(tz0, tz1) + Math.Max(bz0, bz1) + Math.Max(nz0, nz1));
            }
            double pad = 1e-9 * (Math.Abs(lx) + Math.Abs(ly) + Math.Abs(lz) + Math.Abs(hx) + Math.Abs(hy) + Math.Abs(hz) + 1);
            low = new SdfVector(lx - pad, ly - pad, lz - pad);
            high = new SdfVector(hx + pad, hy + pad, hz + pad);
        }

        // The ray enters the box at tEnter and leaves at tExit (inside [0, tMax]); false when it misses
        private static bool Slab(SdfVector o, SdfVector w, SdfVector low, SdfVector high, double tMax, out double tEnter)
        {
            double t0 = 0, t1 = tMax;
            for (int axis = 0; axis < 3; ++axis)
            {
                double origin = axis == 0 ? o.x : axis == 1 ? o.y : o.z;
                double direction = axis == 0 ? w.x : axis == 1 ? w.y : w.z;
                double lo = axis == 0 ? low.x : axis == 1 ? low.y : low.z;
                double hi = axis == 0 ? high.x : axis == 1 ? high.y : high.z;
                if (direction == 0)
                {
                    if (origin < lo || origin > hi)
                    {
                        tEnter = 0;
                        return false;
                    }
                    continue;
                }
                double inverse = 1 / direction;
                double near = (lo - origin) * inverse, far = (hi - origin) * inverse;
                if (near > far)
                {
                    double swap = near;
                    near = far;
                    far = swap;
                }
                t0 = Math.Max(t0, near);
                t1 = Math.Min(t1, far);
                if (t0 > t1)
                {
                    tEnter = 0;
                    return false;
                }
            }
            tEnter = t0;
            return true;
        }

        // Intersects the two micro triangles of cell (i, j), clipped to the base triangle, with the ray; updates the hit when one is
        // closer than hit.t
        private bool IntersectCell(VectorWorkspace ws, SdfVector o, SdfVector w, int i, int j, ref VectorHit hit)
        {
            ShellVectorField field = mesh.field;
            double[] vv = ws.cellVectors;
            for (int corner = 0; corner < 4; ++corner)
            {
                field.Sample(i + (corner & 1), j + ((corner >> 1) & 1), out vv[3 * corner], out vv[3 * corner + 1], out vv[3 * corner + 2]);
            }
            bool found = false;
            // Corners: 0 (i, j), 1 (i+1, j), 2 (i, j+1), 3 (i+1, j+1); triangle 0: (0, 1, 3), triangle 1: (0, 3, 2) as for the heights
            for (int m = 0; m < 2; ++m)
            {
                double[] polygon = ws.polygonA;
                bool inside = true;
                for (int k = 0; k < 3; ++k)
                {
                    int corner = m == 0 ? (k == 0 ? 0 : k == 1 ? 1 : 3) : (k == 0 ? 0 : k == 1 ? 3 : 2);
                    double x = i + (corner & 1), y = j + ((corner >> 1) & 1);
                    GridToBarycentric(x, y, out double a, out double b);
                    polygon[7 * k] = x;
                    polygon[7 * k + 1] = y;
                    polygon[7 * k + 2] = a;
                    polygon[7 * k + 3] = b;
                    polygon[7 * k + 4] = vv[3 * corner];
                    polygon[7 * k + 5] = vv[3 * corner + 1];
                    polygon[7 * k + 6] = vv[3 * corner + 2];
                    inside &= a >= 0 && b >= 0 && a + b <= 1;
                }
                double[] clipped = polygon;
                int count = 3;
                if (!inside)
                {
                    count = ClipToTriangle(ws, 3, out clipped);
                    if (count < 3)
                    {
                        continue;
                    }
                }
                SdfVector[] embedded = ws.embedded;
                for (int k = 0; k < count; ++k)
                {
                    embedded[k] = Embed(clipped[7 * k + 2], clipped[7 * k + 3], clipped[7 * k + 4], clipped[7 * k + 5], clipped[7 * k + 6]);
                }
                for (int k = 1; k + 1 < count; ++k)
                {
                    if (IntersectTriangle(o, w, embedded[0], embedded[k], embedded[k + 1], ref hit.t, out double s, out double t))
                    {
                        found = true;
                        hit.x0 = embedded[0];
                        hit.x1 = embedded[k];
                        hit.x2 = embedded[k + 1];
                        hit.gx0 = clipped[0];
                        hit.gy0 = clipped[1];
                        hit.gx1 = clipped[7 * k];
                        hit.gy1 = clipped[7 * k + 1];
                        hit.gx2 = clipped[7 * (k + 1)];
                        hit.gy2 = clipped[7 * (k + 1) + 1];
                        hit.hitX = (1 - s - t) * hit.gx0 + s * hit.gx1 + t * hit.gx2;
                        hit.hitY = (1 - s - t) * hit.gy0 + s * hit.gy1 + t * hit.gy2;
                        hit.cellX = i;
                        hit.cellY = j;
                    }
                }
            }
            return found;
        }

        // Sutherland-Hodgman against a >= 0, b >= 0 and 1 - a - b >= 0 of a polygon of (x, y, a, b, vx, vy, vz); the result is in
        // ws.polygonA or ws.polygonB
        private static int ClipToTriangle(VectorWorkspace ws, int count, out double[] result)
        {
            double[] source = ws.polygonA, target = ws.polygonB;
            for (int plane = 0; plane < 3; ++plane)
            {
                // Quick accept: all inside
                int outputCount = 0;
                for (int k = 0; k < count; ++k)
                {
                    int next = (k + 1) % count;
                    double dk = PlaneDistance(source, k, plane), dn = PlaneDistance(source, next, plane);
                    if (dk >= 0)
                    {
                        Array.Copy(source, 7 * k, target, 7 * outputCount, 7);
                        ++outputCount;
                    }
                    if ((dk >= 0) != (dn >= 0))
                    {
                        double f = dk / (dk - dn);
                        for (int c = 0; c < 7; ++c)
                        {
                            target[7 * outputCount + c] = source[7 * k + c] + f * (source[7 * next + c] - source[7 * k + c]);
                        }
                        ++outputCount;
                    }
                }
                count = outputCount;
                double[] swap = source;
                source = target;
                target = swap;
                if (count < 3)
                {
                    result = source;
                    return 0;
                }
            }
            result = source;
            return count;
        }

        private static double PlaneDistance(double[] polygon, int vertex, int plane)
        {
            double a = polygon[7 * vertex + 2], b = polygon[7 * vertex + 3];
            return plane == 0 ? a : plane == 1 ? b : 1 - a - b;
        }

        // Moller-Trumbore in double precision with a tolerance at the edges; updates tBest when the hit is closer
        private static bool IntersectTriangle(SdfVector o, SdfVector w, SdfVector p0, SdfVector p1, SdfVector p2, ref double tBest,
                                              out double s, out double t)
        {
            s = 0;
            t = 0;
            SdfVector e1 = p1 - p0, e2 = p2 - p0;
            SdfVector pvec = SdfVector.Cross(w, e2);
            double determinant = SdfVector.Dot(e1, pvec);
            if (Math.Abs(determinant) < 1e-300)
            {
                return false;
            }
            double inverse = 1 / determinant;
            SdfVector tvec = o - p0;
            s = SdfVector.Dot(tvec, pvec) * inverse;
            const double edgeTolerance = 1e-9;
            if (s < -edgeTolerance || s > 1 + edgeTolerance)
            {
                return false;
            }
            SdfVector qvec = SdfVector.Cross(tvec, e1);
            t = SdfVector.Dot(w, qvec) * inverse;
            if (t < -edgeTolerance || s + t > 1 + edgeTolerance)
            {
                return false;
            }
            double distance = SdfVector.Dot(e2, qvec) * inverse;
            if (distance <= 0 || distance >= tBest)
            {
                return false;
            }
            tBest = distance;
            return true;
        }

        private bool Trace(Ray ray, double tMaxRay, bool anyHit, out VectorHit best)
        {
            best = default;
            Point3f oo = mesh.objectFromRender.Apply(ray.o);
            Vector3f ww = mesh.objectFromRender.Apply(ray.d);
            SdfVector o = new SdfVector(oo.x, oo.y, oo.z), w = new SdfVector(ww.x, ww.y, ww.z);
            VectorWorkspace ws = Workspace();
            ShellVectorField field = mesh.field;
            best.t = tMaxRay;
            bool found = false;
            ws.statistics.traces++;
            int top = 0;
            ws.stackLevel[0] = field.levels - 1;
            ws.stackI[0] = 0;
            ws.stackJ[0] = 0;
            ws.stackT[0] = 0;
            top = 1;
            int[] childLevel = ws.childLevel, childI = ws.childI, childJ = ws.childJ;
            double[] childT = ws.childT;
            while (top > 0)
            {
                --top;
                int level = ws.stackLevel[top], i = ws.stackI[top], j = ws.stackJ[top];
                if (ws.stackT[top] >= best.t)
                {
                    continue;
                }
                ws.statistics.nodes++;
                if (level == 0)
                {
                    ws.statistics.leaves++;
                    if (IntersectCell(ws, o, w, i, j, ref best))
                    {
                        found = true;
                        if (anyHit)
                        {
                            return true;
                        }
                    }
                    continue;
                }
                int childCount = 0;
                for (int cj = 2 * j; cj <= 2 * j + 1; ++cj)
                {
                    for (int ci = 2 * i; ci <= 2 * i + 1; ++ci)
                    {
                        if (ci >= field.LevelWidth(level - 1) || cj >= field.LevelHeight(level - 1))
                        {
                            continue;
                        }
                        int size = 1 << (level - 1);
                        if (!RectangleTouchesTriangle(ci * size, cj * size, (ci + 1) * size, (cj + 1) * size))
                        {
                            continue;
                        }
                        NodeSpace(ws, level - 1, ci, cj, out SdfVector low, out SdfVector high);
                        if (!Slab(o, w, low, high, best.t, out double tEnter))
                        {
                            continue;
                        }
                        // Insertion sort by decreasing entry so that the nearest is popped first
                        int position = childCount++;
                        while (position > 0 && childT[position - 1] < tEnter)
                        {
                            childLevel[position] = childLevel[position - 1];
                            childI[position] = childI[position - 1];
                            childJ[position] = childJ[position - 1];
                            childT[position] = childT[position - 1];
                            --position;
                        }
                        childLevel[position] = level - 1;
                        childI[position] = ci;
                        childJ[position] = cj;
                        childT[position] = tEnter;
                    }
                }
                for (int k = 0; k < childCount; ++k)
                {
                    ws.stackLevel[top] = childLevel[k];
                    ws.stackI[top] = childI[k];
                    ws.stackJ[top] = childJ[k];
                    ws.stackT[top] = childT[k];
                    ++top;
                }
            }
            return found;
        }

        // The vertex normal of the displaced surface at a grid point (central differences, oriented to the side of the base normal)
        private SdfVector GridNormal(int x, int y)
        {
            ShellVectorField field = mesh.field;
            SdfVector[] neighbor = new SdfVector[4];
            int[] dx = new int[] { 1, -1, 0, 0 }, dy = new int[] { 0, 0, 1, -1 };
            for (int k = 0; k < 4; ++k)
            {
                int gx = Math.Max(0, Math.Min(field.cellsX, x + dx[k])), gy = Math.Max(0, Math.Min(field.cellsY, y + dy[k]));
                field.Sample(gx, gy, out double vx, out double vy, out double vz);
                GridToBarycentric(gx, gy, out double a, out double b);
                neighbor[k] = Embed(a, b, vx, vy, vz);
            }
            SdfVector cross = SdfVector.Cross(neighbor[0] - neighbor[1], neighbor[2] - neighbor[3]) * mesh.normalSign[triIndex];
            return cross.LengthSquared() > 0 ? cross.Normalized() : new SdfVector(0, 0, 0);
        }

        private SurfaceInteraction InteractionFromHit(Ray ray, VectorHit hit)
        {
            ShellVectorField field = mesh.field;
            SdfVector e1 = hit.x1 - hit.x0, e2 = hit.x2 - hit.x0;
            double dx1 = hit.gx1 - hit.gx0, dy1 = hit.gy1 - hit.gy0, dx2 = hit.gx2 - hit.gx0, dy2 = hit.gy2 - hit.gy0;
            double determinant = dx1 * dy2 - dx2 * dy1;
            SdfVector dXdx = (e1 * dy2 - e2 * dy1) * (1 / determinant), dXdy = (e2 * dx1 - e1 * dx2) * (1 / determinant);
            SdfVector geometric = SdfVector.Cross(e1, e2) * ((determinant >= 0 ? 1 : -1) * mesh.normalSign[triIndex]);
            geometric = geometric.Normalized();

            // The hit point, from the ray (the triangle plane contains it)
            SdfVector objectPoint = new SdfVector(ray.o.x, ray.o.y, ray.o.z);
            {
                Point3f oo = mesh.objectFromRender.Apply(ray.o);
                Vector3f ww = mesh.objectFromRender.Apply(ray.d);
                objectPoint = new SdfVector(oo.x + hit.t * ww.x, oo.y + hit.t * ww.y, oo.z + hit.t * ww.z);
            }

            SdfVector shading = geometric;
            if (mesh.smoothNormals)
            {
                // Bilinear blend of the normals at the four corners of the cell
                double fx = hit.hitX - hit.cellX, fy = hit.hitY - hit.cellY;
                fx = Math.Max(0, Math.Min(1, fx));
                fy = Math.Max(0, Math.Min(1, fy));
                SdfVector blended = GridNormal(hit.cellX, hit.cellY) * ((1 - fx) * (1 - fy)) + GridNormal(hit.cellX + 1, hit.cellY) * (fx * (1 - fy)) +
                                    GridNormal(hit.cellX, hit.cellY + 1) * ((1 - fx) * fy) + GridNormal(hit.cellX + 1, hit.cellY + 1) * (fx * fy);
                if (blended.LengthSquared() > 1e-12)
                {
                    shading = blended.Normalized();
                    if (SdfVector.Dot(shading, geometric) < 0)
                    {
                        shading = -shading;
                    }
                }
            }

            SdfVector dpdu = dXdx * field.cellsX, dpdv = dXdy * field.cellsY;
            Point2f uv = new Point2f((Float)(mesh.tileU[triIndex] + hit.hitX / field.cellsX), (Float)(mesh.tileV[triIndex] + hit.hitY / field.cellsY));

            Transform renderFromObject = mesh.renderFromObject;
            Point3f p = renderFromObject.Apply(new Point3f((Float)objectPoint.x, (Float)objectPoint.y, (Float)objectPoint.z));
            Vector3f pu = renderFromObject.Apply(new Vector3f((Float)dpdu.x, (Float)dpdu.y, (Float)dpdu.z));
            Vector3f pv = renderFromObject.Apply(new Vector3f((Float)dpdv.x, (Float)dpdv.y, (Float)dpdv.z));
            Normal3f ng = pbrt.Normalize(renderFromObject.Apply(new Normal3f((Float)geometric.x, (Float)geometric.y, (Float)geometric.z)));
            Normal3f ns = pbrt.Normalize(renderFromObject.Apply(new Normal3f((Float)shading.x, (Float)shading.y, (Float)shading.z)));
            if (mesh.reverseOrientation)
            {
                ng = -ng;
                ns = -ns;
            }
            Vector3f pError = pbrt.gamma(8) * pbrt.Abs(new Vector3f(p.x, p.y, p.z));
            SurfaceInteraction si = new SurfaceInteraction(new Point3fi(p, pError), uv, -ray.d, pu, pv, new Normal3f(0, 0, 0),
                                                           new Normal3f(0, 0, 0), ray.time, false);
            si.n = ng;
            si.SetShadingGeometry(ns, pu, pv, new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), true);
            return si;
        }

        private readonly VectorDisplacedMesh mesh;
        private readonly int triIndex;
        private readonly Bounds3f bounds;
    }

    // Vector displacement of a tessellated mesh: the refined vertices move by the vector of the map in their frames
    public static class VectorTessellation
    {
        // Displaces the vertices of a refined mesh; n is replaced by the recomputed normals by the caller
        public static void Apply(TriQuadMesh mesh, VectorMapImage image, bool tangentSpace)
        {
            int vertexCount = (int)mesh.p.size();
            Point3f[] p = mesh.p.data();
            Normal3f[] n = mesh.n.data();
            Point2f[] uv = mesh.uv.data();
            Normal3f[] unit = new Normal3f[vertexCount];
            for (int i = 0; i < vertexCount; ++i)
            {
                unit[i] = pbrt.Normalize(n[i]);
            }
            SdfVector[] tangents = null;
            double[] handedness = null;
            if (tangentSpace)
            {
                int[] indices = mesh.triIndices.ToArray();
                VectorFrames.Compute(p, unit, uv, indices, vertexCount, out tangents, out handedness);
            }
            Point3f[] displaced = new Point3f[vertexCount];
            pbrt.ParallelFor(0, vertexCount, (int i) =>
            {
                // Tiles repeat: the fractional part of the uv would lose the map at u = 1, so only the integer tile is removed
                double u = uv[i].x - Math.Floor(uv[i].x), v = uv[i].y - Math.Floor(uv[i].y);
                if (uv[i].x == Math.Floor(uv[i].x) && uv[i].x > 0)
                {
                    u = 1;
                }
                if (uv[i].y == Math.Floor(uv[i].y) && uv[i].y > 0)
                {
                    v = 1;
                }
                image.Bilinear(u, v, out double vx, out double vy, out double vz);
                SdfVector delta;
                if (tangentSpace)
                {
                    SdfVector normal = new SdfVector(unit[i].x, unit[i].y, unit[i].z);
                    SdfVector bitangent = SdfVector.Cross(normal, tangents[i]) * handedness[i];
                    delta = tangents[i] * vx + bitangent * vy + normal * vz;
                }
                else
                {
                    delta = new SdfVector(vx, vy, vz);
                }
                displaced[i] = new Point3f((Float)(p[i].x + delta.x), (Float)(p[i].y + delta.y), (Float)(p[i].z + delta.z));
            });
            for (int i = 0; i < vertexCount; ++i)
            {
                p[i] = displaced[i];
            }
        }
    }

}  // namespace pbrt
