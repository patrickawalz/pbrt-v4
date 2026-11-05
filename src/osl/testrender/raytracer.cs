// OSL port: testrender/raytracer.h, bvh.h, bvh.cpp and scene.cpp
//
// Rays, the pinhole camera, and the triangle scene with its bounding volume hierarchy (built with the binned surface area heuristic, traversed with the
// watertight triangle test).

using System;
using System.Collections.Generic;
using OIIO;

namespace OSL
{
    public struct DualVec3
    {
        public Vec3 val, dx, dy;
    }

    public struct DualVec2
    {
        public Vec2f val, dx, dy;
    }

    public sealed class Ray
    {
        public const int CAMERA = 1;
        public const int SHADOW = 2;
        public const int REFLECTION = 4;
        public const int REFRACTION = 8;
        public const int DIFFUSE = 16;
        public const int GLOSSY = 32;
        public const int SUBSURFACE = 64;
        public const int DISPLACEMENT = 128;

        public Vec3 origin, direction;
        public float radius, spread, roughness;
        public int raytype;

        public Ray(Vec3 o, Vec3 d, float radius, float spread, float roughness, int raytype)
        {
            origin = o;
            direction = d;
            this.radius = radius;
            this.spread = spread;
            this.roughness = roughness;
            this.raytype = raytype;
        }

        public static void ortho(Vec3 n, out Vec3 x, out Vec3 y)
        {
            float sign = MathF.CopySign(1.0f, n.z);
            float a = -1.0f / (sign + n.z);
            float b = n.x * n.y * a;
            x = new Vec3(1.0f + sign * n.x * n.x * a, sign * b, -sign * n.x);
            y = new Vec3(b, sign + n.y * n.y * a, -n.y);
        }

        public Vec3 point(float t)
        {
            return origin + direction * t;
        }

        public DualVec3 dual_direction()
        {
            DualVec3 v;
            v.val = direction;
            ortho(direction, out v.dx, out v.dy);
            v.dx *= spread;
            v.dy *= spread;
            return v;
        }

        /// The point at distance t along the ray, with its footprint; dt is the derivative of t (unused, as in the C++ where it is dropped)
        public DualVec3 point_dual(float t)
        {
            float r = radius + spread * t;
            DualVec3 p;
            p.val = point(t);
            ortho(direction, out p.dx, out p.dy);
            p.dx *= r;
            p.dy *= r;
            return p;
        }
    }

    public sealed class Camera
    {
        public Vec3 eye = new Vec3(0, 0, 0);
        public Vec3 dir = new Vec3(0, 0, -1);
        public Vec3 up = new Vec3(0, 1, 0);
        public float fov = 90;
        public int xres = 1;
        public int yres = 1;
        public Vec3 cx, cy;
        public float invw, invh;

        public void lookat(Vec3 eye, Vec3 dir, Vec3 up, float fov)
        {
            this.eye = eye;
            this.dir = V3.normalized(dir);
            this.up = up;
            this.fov = fov;
            finalize();
        }

        public void resolution(int w, int h)
        {
            xres = w;
            yres = h;
            invw = 1.0f / w;
            invh = 1.0f / h;
            finalize();
        }

        public void finalize()
        {
            float k = fmath.fast_tan(fov * (float)(Math.PI / 360));
            Vec3 right = V3.normalize(dir.cross(up));
            cx = right * (xres * k / yres);
            cy = V3.normalize(cx.cross(dir)) * k;
        }

        public Ray get(float x, float y)
        {
            Vec3 v = V3.normalize(cx * (x * invw - 0.5f) + cy * (0.5f - y * invh) + dir);
            float cos_a = dir.dot(v);
            float spread = MathF.Sqrt(invw * invh * cx.length() * cy.length() * cos_a) * cos_a;
            return new Ray(eye, v, 0, spread, 0.0f, Ray.CAMERA);
        }
    }

    public struct TriangleIndices
    {
        public int a, b, c;

        public TriangleIndices(int a, int b, int c)
        {
            this.a = a;
            this.b = b;
            this.c = c;
        }
    }

    public struct LightSample
    {
        public Vec3 dir;
        public float dist;
        public float pdf;
        public float u, v;
    }

    public struct Intersection
    {
        public float t, u, v;
        public uint id;
    }

    public struct BVHNode
    {
        public float b0, b1, b2, b3, b4, b5;  // x lo, x hi, y lo, y hi, z lo, z hi
        public uint child, nprims;

        public void set(Vec3 lo, Vec3 hi)
        {
            b0 = lo.x;
            b1 = hi.x;
            b2 = lo.y;
            b3 = hi.y;
            b4 = lo.z;
            b5 = hi.z;
        }

        public float half_area()
        {
            float vx = b1 - b0;
            float vy = b3 - b2;
            float vz = b5 - b4;
            return vx * vy + vy * vz + vz * vx;
        }
    }

    /// Imath::Box3f with the empty box that extendBy grows
    public struct Box3
    {
        public Vec3 min, max;

        public static Box3 empty()
        {
            Box3 b;
            b.min = new Vec3(float.MaxValue, float.MaxValue, float.MaxValue);
            b.max = new Vec3(-float.MaxValue, -float.MaxValue, -float.MaxValue);
            return b;
        }

        public void extendBy(Vec3 p)
        {
            if (p.x < min.x)
            {
                min.x = p.x;
            }
            if (p.x > max.x)
            {
                max.x = p.x;
            }
            if (p.y < min.y)
            {
                min.y = p.y;
            }
            if (p.y > max.y)
            {
                max.y = p.y;
            }
            if (p.z < min.z)
            {
                min.z = p.z;
            }
            if (p.z > max.z)
            {
                max.z = p.z;
            }
        }

        public void extendBy(Box3 box)
        {
            if (box.min.x < min.x)
            {
                min.x = box.min.x;
            }
            if (box.max.x > max.x)
            {
                max.x = box.max.x;
            }
            if (box.min.y < min.y)
            {
                min.y = box.min.y;
            }
            if (box.max.y > max.y)
            {
                max.y = box.max.y;
            }
            if (box.min.z < min.z)
            {
                min.z = box.min.z;
            }
            if (box.max.z > max.z)
            {
                max.z = box.max.z;
            }
        }

        public Vec3 center()
        {
            return (max + min) / 2.0f;
        }

        public float half_area()
        {
            Vec3 d = max - min;
            return d.x * d.y + d.y * d.z + d.z * d.x;
        }
    }

    public sealed class BVH
    {
        public BVHNode[] nodes;
        public uint[] indices;
    }

    public sealed class ShaderMap : Dictionary<string, int>
    {
    }

    public sealed unsafe class Scene
    {
        private const int NumBins = 16;
        private const int MaxDepth = 64;

        public List<Vec3> verts = new List<Vec3>();
        public List<Vec3> normals = new List<Vec3>();
        public List<Vec2f> uvs = new List<Vec2f>();
        public List<TriangleIndices> triangles = new List<TriangleIndices>();
        public List<TriangleIndices> uv_triangles = new List<TriangleIndices>();
        public List<TriangleIndices> n_triangles = new List<TriangleIndices>();
        public List<int> shaderids = new List<int>();
        public List<int> last_index = new List<int>();  // one entry per mesh: the last triangle index (+1), also the first triangle of the next mesh
        public BVH bvh;

        private Vec3[] m_verts;
        private TriangleIndices[] m_triangles;

        public int num_prims()
        {
            return triangles.Count;
        }

        public void add_sphere(Vec3 c, float r, int shaderID, int resolution)
        {
            int W = 2 * resolution;
            int H = resolution;
            int NV = 2 + W * H;
            int base_idx = verts.Count;
            int n_base_idx = normals.Count;
            int t_base_idx = uvs.Count;
            verts.Add(c + new Vec3(0, r, 0));
            normals.Add(new Vec3(0, 1, 0));
            for (int y = 0; y < H; y++)
            {
                float t = (float)(y + 0.5f) / (float)H;
                float z = MathF.Cos(t * (float)Math.PI);
                float q = MathF.Sqrt(MathF.Max(0.0f, 1.0f - z * z));
                for (int x = 0; x < W; x++)
                {
                    float a = (float)(2 * Math.PI) * (float)x / (float)W;
                    Vec3 n = new Vec3(q * -MathF.Sin(a), z, q * -MathF.Cos(a));
                    verts.Add(c + r * n);
                    normals.Add(n);
                }
            }
            verts.Add(c - new Vec3(0, r, 0));
            normals.Add(new Vec3(0, -1, 0));
            for (int y = 0; y < 2; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    float s = (float)(x + 0.5f) / (float)W;
                    uvs.Add(new Vec2f(s, y));
                }
            }
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x <= W; x++)
                {
                    float s = (float)x / (float)W;
                    float t = (float)(y + 0.5f) / (float)H;
                    uvs.Add(new Vec2f(s, t));
                }
            }
            for (int x0 = 0, x1 = W - 1; x0 < W; x1 = x0, x0++)
            {
                triangles.Add(new TriangleIndices(base_idx, base_idx + 1 + x1, base_idx + 1 + x0));
                n_triangles.Add(new TriangleIndices(n_base_idx, n_base_idx + 1 + x1, n_base_idx + 1 + x0));
                uv_triangles.Add(new TriangleIndices(t_base_idx + x1, t_base_idx + 2 * W + x1, t_base_idx + 2 * W + x1 + 1));
                shaderids.Add(shaderID);
                for (int y = 0; y < H - 1; y++)
                {
                    int i00 = 1 + (x0 + W * (y + 0));
                    int i10 = 1 + (x1 + W * (y + 0));
                    int i11 = 1 + (x1 + W * (y + 1));
                    int i01 = 1 + (x0 + W * (y + 1));
                    triangles.Add(new TriangleIndices(base_idx + i00, base_idx + i10, base_idx + i11));
                    triangles.Add(new TriangleIndices(base_idx + i00, base_idx + i11, base_idx + i01));
                    n_triangles.Add(new TriangleIndices(n_base_idx + i00, n_base_idx + i10, n_base_idx + i11));
                    n_triangles.Add(new TriangleIndices(n_base_idx + i00, n_base_idx + i11, n_base_idx + i01));
                    int t00 = 2 * W + x1 + 1 + (W + 1) * (y + 0);
                    int t10 = 2 * W + x1 + (W + 1) * (y + 0);
                    int t11 = 2 * W + x1 + (W + 1) * (y + 1);
                    int t01 = 2 * W + x1 + 1 + (W + 1) * (y + 1);
                    uv_triangles.Add(new TriangleIndices(t_base_idx + t00, t_base_idx + t10, t_base_idx + t11));
                    uv_triangles.Add(new TriangleIndices(t_base_idx + t00, t_base_idx + t11, t_base_idx + t01));
                    shaderids.Add(shaderID);
                    shaderids.Add(shaderID);
                }
                triangles.Add(new TriangleIndices(base_idx + NV - 1, base_idx + NV - 1 - W + x0, base_idx + NV - 1 - W + x1));
                n_triangles.Add(new TriangleIndices(n_base_idx + NV - 1, n_base_idx + NV - 1 - W + x0, n_base_idx + NV - 1 - W + x1));
                uv_triangles.Add(new TriangleIndices(t_base_idx + W + x1, t_base_idx + 2 * W + x1 + 1 + (W + 1) * (H - 1),
                                                     t_base_idx + 2 * W + x1 + (W + 1) * (H - 1)));
                shaderids.Add(shaderID);
            }
            last_index.Add(triangles.Count);
        }

        public void add_quad(Vec3 p, Vec3 ex, Vec3 ey, int shaderID, int resolution)
        {
            int base_idx = verts.Count;
            int t_base_idx = uvs.Count;
            for (int v = 0; v <= resolution; v++)
            {
                for (int u = 0; u <= resolution; u++)
                {
                    float s = (float)u / (float)resolution;
                    float t = (float)v / (float)resolution;
                    verts.Add(p + s * ex + t * ey);
                    uvs.Add(new Vec2f(s, t));
                }
            }
            for (int v = 0; v < resolution; v++)
            {
                for (int u = 0; u < resolution; u++)
                {
                    int i00 = (u + 0) + (v + 0) * (resolution + 1);
                    int i10 = (u + 1) + (v + 0) * (resolution + 1);
                    int i11 = (u + 1) + (v + 1) * (resolution + 1);
                    int i01 = (u + 0) + (v + 1) * (resolution + 1);
                    triangles.Add(new TriangleIndices(base_idx + i00, base_idx + i10, base_idx + i11));
                    triangles.Add(new TriangleIndices(base_idx + i00, base_idx + i11, base_idx + i01));
                    n_triangles.Add(new TriangleIndices(-1, -1, -1));
                    n_triangles.Add(new TriangleIndices(-1, -1, -1));
                    uv_triangles.Add(new TriangleIndices(t_base_idx + i00, t_base_idx + i10, t_base_idx + i11));
                    uv_triangles.Add(new TriangleIndices(t_base_idx + i00, t_base_idx + i11, t_base_idx + i01));
                    shaderids.Add(shaderID);
                    shaderids.Add(shaderID);
                }
            }
            last_index.Add(triangles.Count);
        }

        public void add_model(string filename, ShaderMap shadermap, int shaderID, ErrorHandler errhandler)
        {
            ObjFile obj = ObjFile.parse(filename, errhandler);
            if (obj == null)
            {
                return;
            }
            List<int> material_ids = new List<int>();
            bool use_material_ids = false;
            if (shadermap.Count != 0)
            {
                foreach (string mat in obj.materials)
                {
                    int found;
                    if (shadermap.TryGetValue(mat, out found))
                    {
                        errhandler.infofmt("Found material {}", mat);
                        material_ids.Add(found);
                        use_material_ids = true;
                    }
                    else
                    {
                        material_ids.Add(shaderID);
                        errhandler.infofmt("Material {} did not match any shader names", mat);
                    }
                }
            }
            int nverts = obj.positions.Count / 3;
            int base_idx = verts.Count;
            for (int i = 0, i3 = 0; i < nverts; i++, i3 += 3)
            {
                verts.Add(new Vec3(obj.positions[i3 + 0], obj.positions[i3 + 1], obj.positions[i3 + 2]));
            }
            int n_base_idx = normals.Count;
            int nnorms = obj.normals.Count / 3;
            for (int i = 0, i3 = 0; i < nnorms; i++, i3 += 3)
            {
                normals.Add(new Vec3(obj.normals[i3 + 0], obj.normals[i3 + 1], obj.normals[i3 + 2]));
            }
            int uv_base_idx = uvs.Count;
            int nuvs = obj.texcoords.Count / 2;
            for (int i = 0, i2 = 0; i < nuvs; i++, i2 += 2)
            {
                uvs.Add(new Vec2f(obj.texcoords[i2 + 0], obj.texcoords[i2 + 1]));
            }
            int ntris = 0;
            foreach (ObjShape shape in obj.shapes)
            {
                int shapeShaderID = shaderID;
                if (!use_material_ids)
                {
                    int found;
                    if (shadermap.TryGetValue(shape.name, out found))
                    {
                        errhandler.infofmt("Found mesh {}", shape.name);
                        shapeShaderID = found;
                    }
                    else
                    {
                        errhandler.infofmt("Mesh {} did not match any shader names", shape.name);
                    }
                }
                for (int i = 0, n = shape.indices.Count, f = 0; i < n; i += 3, f++)
                {
                    ObjIndex i0 = shape.indices[i + 0];
                    ObjIndex i1 = shape.indices[i + 1];
                    ObjIndex i2 = shape.indices[i + 2];
                    triangles.Add(new TriangleIndices(base_idx + i0.position, base_idx + i1.position, base_idx + i2.position));
                    n_triangles.Add(new TriangleIndices(i0.normal < 0 ? -1 : n_base_idx + i0.normal, i0.normal < 0 ? -1 : n_base_idx + i1.normal,
                                                        i0.normal < 0 ? -1 : n_base_idx + i2.normal));
                    uv_triangles.Add(new TriangleIndices(i0.texcoord < 0 ? -1 : uv_base_idx + i0.texcoord,
                                                         i0.texcoord < 0 ? -1 : uv_base_idx + i1.texcoord,
                                                         i0.texcoord < 0 ? -1 : uv_base_idx + i2.texcoord));
                    if (use_material_ids && shape.material_ids.Count != 0)
                    {
                        shaderids.Add(material_ids[shape.material_ids[f]]);
                    }
                    else
                    {
                        shaderids.Add(shapeShaderID);
                    }
                    ntris++;
                }
                last_index.Add(triangles.Count);
            }
            errhandler.infofmt("Parsed {} vertices and {} triangles from {} in {}", nverts, ntris, filename, "0.00s");
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // Shape sampling and surface queries
        // ------------------------------------------------------------------------------------------------------------------------------------

        public LightSample sample(int primID, Vec3 x, float xi, float yi)
        {
            if (yi > xi)
            {
                xi *= 0.5f;
                yi -= xi;
            }
            else
            {
                yi *= 0.5f;
                xi -= yi;
            }
            Vec3 va = verts[triangles[primID].a];
            Vec3 vb = verts[triangles[primID].b];
            Vec3 vc = verts[triangles[primID].c];
            Vec3 n = (va - vb).cross(va - vc);
            Vec3 l = ((1 - xi - yi) * va + xi * vb + yi * vc) - x;
            float d2 = l.length2();
            Vec3 dir = V3.normalize(l);
            float pdf = d2 / (0.5f * MathF.Abs(dir.dot(n)));
            LightSample s;
            s.dir = dir;
            s.dist = MathF.Sqrt(d2);
            s.pdf = pdf;
            s.u = xi;
            s.v = yi;
            return s;
        }

        public float shapepdf(int primID, Vec3 x, Vec3 p)
        {
            Vec3 va = verts[triangles[primID].a];
            Vec3 vb = verts[triangles[primID].b];
            Vec3 vc = verts[triangles[primID].c];
            Vec3 n = (va - vb).cross(va - vc);
            Vec3 l = p - x;
            float d2 = l.length2();
            Vec3 dir = V3.normalize(l);
            return d2 / (0.5f * MathF.Abs(dir.dot(n)));
        }

        public float primitivearea(int primID)
        {
            Vec3 va = verts[triangles[primID].a];
            Vec3 vb = verts[triangles[primID].b];
            Vec3 vc = verts[triangles[primID].c];
            return 0.5f * (va - vb).cross(va - vc).length();
        }

        public Vec3 normal(DualVec3 p, out Vec3 Ng, int primID, float u, float v)
        {
            Vec3 va = verts[triangles[primID].a];
            Vec3 vb = verts[triangles[primID].b];
            Vec3 vc = verts[triangles[primID].c];
            Ng = V3.normalize((va - vb).cross(va - vc));
            if (n_triangles[primID].a < 0)
            {
                return Ng;
            }
            Vec3 na = normals[n_triangles[primID].a];
            Vec3 nb = normals[n_triangles[primID].b];
            Vec3 nc = normals[n_triangles[primID].c];
            return V3.normalize((1 - u - v) * na + u * nb + v * nc);
        }

        public void project(ref DualVec3 p, Vec3 N, Vec3 I)
        {
            Vec3 nI = V3.normalized(I);
            float cosI = (-nI).dot(N);
            if (MathF.Abs(cosI) > 1e-3f)
            {
                float deltaX = p.dx.dot(N) / cosI;
                float deltaY = p.dy.dot(N) / cosI;
                p.dx += nI * deltaX;
                p.dy += nI * deltaY;
            }
        }

        public DualVec2 uv(DualVec3 p, Vec3 n, ref Vec3 dPdu, ref Vec3 dPdv, int primID, float u, float v)
        {
            DualVec2 r;
            if (uv_triangles[primID].a < 0)
            {
                r.val = new Vec2f(0, 0);
                r.dx = new Vec2f(0, 0);
                r.dy = new Vec2f(0, 0);
                return r;
            }
            Vec2f ta = uvs[uv_triangles[primID].a];
            Vec2f tb = uvs[uv_triangles[primID].b];
            Vec2f tc = uvs[uv_triangles[primID].c];
            Vec3 va = verts[triangles[primID].a];
            Vec3 vb = verts[triangles[primID].b];
            Vec3 vc = verts[triangles[primID].c];
            Vec2f dt02 = ta - tc;
            Vec2f dt12 = tb - tc;
            Vec3 dp02 = va - vc;
            Vec3 dp12 = vb - vc;
            float det = dt02.x * dt12.y - dt02.y * dt12.x;
            if (det != 0)
            {
                float invdet = 1 / det;
                dPdu = (dt12.y * dp02 - dt02.y * dp12) * invdet;
                dPdv = (-dt12.x * dp02 + dt02.x * dp12) * invdet;
            }
            Vec3 La = n.cross(vc - vb);
            La /= (va - vb).dot(La);
            Vec3 Lb = n.cross(va - vc);
            Lb /= (vb - vc).dot(Lb);
            Vec3 Lc = n.cross(vb - va);
            Lc /= (vc - va).dot(Lc);
            Vec2f dTdx = La.dot(p.dx) * ta + Lb.dot(p.dx) * tb + Lc.dot(p.dx) * tc;
            Vec2f dTdy = La.dot(p.dy) * ta + Lb.dot(p.dy) * tb + Lc.dot(p.dy) * tc;
            r.val = (1 - u - v) * ta + u * tb + v * tc;
            r.dx = dTdx;
            r.dy = dTdy;
            return r;
        }

        public int shaderid(int primID)
        {
            return shaderids[primID];
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // BVH
        // ------------------------------------------------------------------------------------------------------------------------------------

        private struct BuildNode
        {
            public Box3 centroid;
            public uint left, right;
            public uint nodeIndex;
            public int depth;
        }

        private static float comp(Vec3 v, int i)
        {
            return i == 0 ? v.x : (i == 1 ? v.y : v.z);
        }

        public void prepare(ErrorHandler errhandler)
        {
            bvh = build_bvh(errhandler);
            m_verts = verts.ToArray();
            m_triangles = triangles.ToArray();
        }

        private BVH build_bvh(ErrorHandler errhandler)
        {
            BVH result = new BVH();
            int ntri = triangles.Count;
            result.indices = new uint[ntri];
            List<BVHNode> buildnodes = new List<BVHNode>(2 * ntri + 1);
            buildnodes.Add(new BVHNode());
            List<Box3> triangle_bounds = new List<Box3>(ntri);
            BuildNode current = new BuildNode();
            current.centroid = Box3.empty();
            Box3 shape_bounds = Box3.empty();
            for (int i = 0; i < ntri; i++)
            {
                result.indices[i] = (uint)i;
                Vec3 va = verts[triangles[i].a];
                Vec3 vb = verts[triangles[i].b];
                Vec3 vc = verts[triangles[i].c];
                Box3 b = Box3.empty();
                b.extendBy(va);
                b.extendBy(vb);
                b.extendBy(vc);
                triangle_bounds.Add(b);
                current.centroid.extendBy(b.center());
                shape_bounds.extendBy(b);
            }
            BVHNode root = buildnodes[0];
            root.set(shape_bounds.min, shape_bounds.max);
            buildnodes[0] = root;
            current.left = 0;
            current.right = (uint)ntri;
            current.depth = 1;
            current.nodeIndex = 0;
            int stackPtr = 0;
            BuildNode[] stack = new BuildNode[MaxDepth];
            while (true)
            {
                uint numPrims = current.right - current.left;
                if (numPrims > 1 && current.depth < MaxDepth)
                {
                    Box3[,] binBounds = new Box3[3, NumBins];
                    uint[,] binN = new uint[3, NumBins];
                    for (int axis = 0; axis < 3; axis++)
                    {
                        for (int i = 0; i < NumBins; i++)
                        {
                            binBounds[axis, i] = Box3.empty();
                        }
                    }
                    float[] binFactor = new float[3];
                    for (int axis = 0; axis < 3; axis++)
                    {
                        binFactor[axis] = comp(current.centroid.max, axis) - comp(current.centroid.min, axis);
                        binFactor[axis] = (binFactor[axis] > 0) ? (float)(0.999f * NumBins) / binFactor[axis] : 0;
                    }
                    for (uint i = current.left; i < current.right; i++)
                    {
                        uint prim = result.indices[i];
                        Box3 bbox = triangle_bounds[(int)prim];
                        Vec3 center = bbox.center();
                        for (int axis = 0; axis < 3; axis++)
                        {
                            int binID = (int)((comp(center, axis) - comp(current.centroid.min, axis)) * binFactor[axis]);
                            binN[axis, binID]++;
                            binBounds[axis, binID].extendBy(bbox);
                        }
                    }
                    float invArea = 1 / buildnodes[(int)current.nodeIndex].half_area();
                    float bestCost = numPrims;
                    int bestAxis = -1;
                    int bestBin = -1;
                    uint bestNL = 0;
                    uint bestNR = 0;
                    for (int axis = 0; axis < 3; axis++)
                    {
                        if (binFactor[axis] == 0)
                        {
                            continue;
                        }
                        uint countL = 0;
                        Box3 bbox = Box3.empty();
                        uint[] numL = new uint[NumBins];
                        float[] areaL = new float[NumBins];
                        for (int i = 0; i < NumBins; i++)
                        {
                            countL += binN[axis, i];
                            numL[i] = countL;
                            bbox.extendBy(binBounds[axis, i]);
                            areaL[i] = bbox.half_area();
                        }
                        bbox = binBounds[axis, NumBins - 1];
                        for (int i = NumBins - 2; i >= 0; i--)
                        {
                            if (numL[i] == 0 || numL[i] == numPrims)
                            {
                                continue;
                            }
                            float areaR = bbox.half_area();
                            const float trav_cost = 4;
                            float cost = trav_cost + invArea * (areaL[i] * numL[i] + areaR * (numPrims - numL[i]));
                            if (cost < bestCost)
                            {
                                bestCost = cost;
                                bestAxis = axis;
                                bestBin = i;
                                bestNL = numL[i];
                                bestNR = numPrims - bestNL;
                            }
                            bbox.extendBy(binBounds[axis, i]);
                        }
                    }
                    if (bestAxis != -1)
                    {
                        Box3 boundsL = Box3.empty();
                        Box3 boundsR = Box3.empty();
                        BuildNode bn0 = new BuildNode();
                        BuildNode bn1 = new BuildNode();
                        bn0.centroid = Box3.empty();
                        bn1.centroid = Box3.empty();
                        bn0.depth = bn1.depth = current.depth + 1;
                        uint rightOrig = current.right;
                        for (uint i = current.left; i < current.right;)
                        {
                            uint prim = result.indices[i];
                            Box3 bbox = triangle_bounds[(int)prim];
                            float center = comp(bbox.center(), bestAxis);
                            int binID = (int)((center - comp(current.centroid.min, bestAxis)) * binFactor[bestAxis]);
                            if (binID <= bestBin)
                            {
                                boundsL.extendBy(bbox);
                                bn0.centroid.extendBy(bbox.center());
                                i++;
                            }
                            else
                            {
                                boundsR.extendBy(bbox);
                                bn1.centroid.extendBy(bbox.center());
                                uint tmp = result.indices[i];
                                --current.right;
                                result.indices[i] = result.indices[current.right];
                                result.indices[current.right] = tmp;
                            }
                        }
                        uint nextIndex = (uint)buildnodes.Count;
                        buildnodes.Add(new BVHNode());
                        buildnodes.Add(new BVHNode());
                        BVHNode cur = buildnodes[(int)current.nodeIndex];
                        cur.child = nextIndex;
                        cur.nprims = 0;
                        buildnodes[(int)current.nodeIndex] = cur;
                        bn0.left = current.left;
                        bn0.right = current.right;
                        bn1.left = current.right;
                        bn1.right = rightOrig;
                        bn0.nodeIndex = nextIndex + 0;
                        bn1.nodeIndex = nextIndex + 1;
                        BVHNode n0 = new BVHNode();
                        n0.set(boundsL.min, boundsL.max);
                        buildnodes[(int)nextIndex + 0] = n0;
                        BVHNode n1 = new BVHNode();
                        n1.set(boundsR.min, boundsR.max);
                        buildnodes[(int)nextIndex + 1] = n1;
                        current = bn0;
                        stack[stackPtr++] = bn1;
                        continue;
                    }
                }
                BVHNode leaf = buildnodes[(int)current.nodeIndex];
                leaf.child = current.left;
                leaf.nprims = numPrims;
                buildnodes[(int)current.nodeIndex] = leaf;
                if (stackPtr == 0)
                {
                    break;
                }
                current = stack[--stackPtr];
            }
            result.nodes = buildnodes.ToArray();
            errhandler.infofmt("BVH built {} nodes over {} triangles in {}", result.nodes.Length, ntri, "0.00s");
            errhandler.infofmt("Root bounding box {}, {}, {} to {}, {}, {}", shape_bounds.min.x, shape_bounds.min.y, shape_bounds.min.z, shape_bounds.max.x,
                               shape_bounds.max.y, shape_bounds.max.z);
            return result;
        }

        private static float minf(float a, float b)
        {
            return b < a ? b : a;
        }

        private static float maxf(float a, float b)
        {
            return b > a ? b : a;
        }

        private static bool box_intersect(Vec3 org, Vec3 rdir, float tmax, ref BVHNode bounds, out float dist)
        {
            float tx1 = (bounds.b0 - org.x) * rdir.x;
            float tx2 = (bounds.b1 - org.x) * rdir.x;
            float ty1 = (bounds.b2 - org.y) * rdir.y;
            float ty2 = (bounds.b3 - org.y) * rdir.y;
            float tz1 = (bounds.b4 - org.z) * rdir.z;
            float tz2 = (bounds.b5 - org.z) * rdir.z;
            float tmin = minf(tx1, tx2);
            tmax = minf(tmax, maxf(tx1, tx2));
            tmin = maxf(tmin, minf(ty1, ty2));
            tmax = minf(tmax, maxf(ty1, ty2));
            tmin = maxf(tmin, minf(tz1, tz2));
            tmax = minf(tmax, maxf(tz1, tz2));
            dist = tmin;
            tmin = maxf(0.0f, tmin);
            return tmin <= tmax;
        }

        private static uint signmask(float a)
        {
            return (uint)BitConverter.SingleToInt32Bits(a) & 0x80000000u;
        }

        private static float xorf(float a, uint b)
        {
            return BitConverter.Int32BitsToSingle((int)((uint)BitConverter.SingleToInt32Bits(a) ^ b));
        }

        public Intersection intersect(Ray ray, float tmax, uint skipID1, uint skipID2 = ~0u)
        {
            int* stackNode = stackalloc int[MaxDepth];
            float* stackDist = stackalloc float[MaxDepth];
            Intersection result = new Intersection();
            result.t = tmax;
            stackNode[0] = 0;
            stackDist[0] = result.t;
            Vec3 org = ray.origin;
            Vec3 dir = ray.direction;
            Vec3 rdir = new Vec3(1 / dir.x, 1 / dir.y, 1 / dir.z);
            int kz = 0;
            if (MathF.Abs(dir.y) > MathF.Abs(comp(dir, kz)))
            {
                kz = 1;
            }
            if (MathF.Abs(dir.z) > MathF.Abs(comp(dir, kz)))
            {
                kz = 2;
            }
            int kx = kz == 2 ? 0 : kz + 1;
            int ky = kx == 2 ? 0 : kx + 1;
            Vec3 shearDir = new Vec3(comp(dir, kx) / comp(dir, kz), comp(dir, ky) / comp(dir, kz), comp(rdir, kz));
            BVHNode[] nodes = bvh.nodes;
            for (int stackPtr = 1; stackPtr != 0;)
            {
                --stackPtr;
                if (result.t < stackDist[stackPtr])
                {
                    continue;
                }
                int nodeIndex = stackNode[stackPtr];
                if (nodes[nodeIndex].nprims != 0)
                {
                    for (uint i = 0, nprims = nodes[nodeIndex].nprims; i < nprims; i++)
                    {
                        uint id = bvh.indices[nodes[nodeIndex].child + i];
                        Vec3 A = m_verts[m_triangles[id].a] - org;
                        Vec3 B = m_verts[m_triangles[id].b] - org;
                        Vec3 C = m_verts[m_triangles[id].c] - org;
                        float Ax = comp(A, kx) - shearDir.x * comp(A, kz);
                        float Ay = comp(A, ky) - shearDir.y * comp(A, kz);
                        float Bx = comp(B, kx) - shearDir.x * comp(B, kz);
                        float By = comp(B, ky) - shearDir.y * comp(B, kz);
                        float Cx = comp(C, kx) - shearDir.x * comp(C, kz);
                        float Cy = comp(C, ky) - shearDir.y * comp(C, kz);
                        float U = Cx * By - Cy * Bx;
                        float V = Ax * Cy - Ay * Cx;
                        float W = Bx * Ay - By * Ax;
                        if ((U < 0 || V < 0 || W < 0) && (U > 0 || V > 0 || W > 0))
                        {
                            continue;
                        }
                        float det = U + V + W;
                        if (det == 0)
                        {
                            continue;
                        }
                        float Az = comp(A, kz);
                        float Bz = comp(B, kz);
                        float Cz = comp(C, kz);
                        float T = shearDir.z * (U * Az + V * Bz + W * Cz);
                        uint mask = signmask(det);
                        if (xorf(T, mask) < 0)
                        {
                            continue;
                        }
                        if (xorf(T, mask) > result.t * xorf(det, mask))
                        {
                            continue;
                        }
                        if (id == skipID1)
                        {
                            continue;
                        }
                        if (id == skipID2)
                        {
                            continue;
                        }
                        float rcpDet = 1 / det;
                        result.t = T * rcpDet;
                        result.u = V * rcpDet;
                        result.v = W * rcpDet;
                        result.id = id;
                    }
                }
                else
                {
                    int child1 = (int)nodes[nodeIndex].child;
                    int child2 = child1 + 1;
                    float dist1;
                    bool hit1 = box_intersect(org, rdir, result.t, ref nodes[child1], out dist1);
                    float dist2;
                    bool hit2 = box_intersect(org, rdir, result.t, ref nodes[child2], out dist2);
                    if (dist1 > dist2)
                    {
                        bool th = hit1;
                        hit1 = hit2;
                        hit2 = th;
                        float td = dist1;
                        dist1 = dist2;
                        dist2 = td;
                        int tc = child1;
                        child1 = child2;
                        child2 = tc;
                    }
                    stackNode[stackPtr] = child2;
                    stackDist[stackPtr] = dist2;
                    stackPtr += hit2 ? 1 : 0;
                    stackNode[stackPtr] = child1;
                    stackDist[stackPtr] = dist1;
                    stackPtr += hit1 ? 1 : 0;
                }
            }
            return result;
        }
    }
}
