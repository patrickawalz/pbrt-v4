// (not in pbrt-v4) sdfops.cs
//
// The operators and primitives that the Vectron of Octane Render has and sdf.cs did not:
//
//   inset(a, b, depth [, edge])      a pushed in by depth inside b (the pocket wall rises over the distance edge)
//   ink(a, b, width)                 a with a groove of the given width cut along the surface of b
//   clip(a, b [, thickness])         a thin skin of a (open: no cap) that is cut away outside b
//   julia(cx, cy, cz, cw [, iterations, bailout, slice, safety])   the quaternion Julia set
//   metaballs(threshold, x, y, z, radius, strength, ...)           blobs that add up (Wyvill kernel)
//   texdisplace(a, "kind", height [, offset, stepscale, "projection", scale])
//                                    the surface moved along the normal by height * texture + offset; kind is "noise", "fbm", "checker",
//                                    "ripple" or the name of an image file; projection is "xyz" (the point itself), "xy", "yz", "xz",
//                                    "spherical", "cylindrical", "box" or "triplanar"; scale multiplies the coordinates
//   label(a, i)                      names an operand for the materials (see SdfLabelTexture)
//   transfer(a, b)                   the shape of a with the materials of b
//   morph(a, b)  translate_t(a, vx, vy, vz)  rotate_t(a, rx, ry, rz)  displace_t(a, amplitude, frequency, speed [, octaves, roughness])
//   mandelbulbt(power0, power1 [, iterations, bailout, safety])
//                                    fields that change with the time of the ray (0 to 1 over the shutter), for motion blur
//
// Materials: the weight of every labelled operand at a point (smooth across the radius of a smooth union) is what Texture "sdflabel"
// returns for "integer label", so a material "mix" with that texture as its amount blends the materials of the operands.

using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace pbrt
{

    // SdfTime Definition: the time of the ray that is being traced (the field of a motion blurred shape is a function of it)
    public static class SdfTime
    {
        [ThreadStatic]
        public static double Current;

        public static double Clamped()
        {
            return Math.Min(Math.Max(Current, 0), 1);
        }
    }

    // SdfWeights Definition: the share of every label at a point
    public sealed class SdfWeights
    {
        public void Add(int label, double weight)
        {
            while (values.Count <= label)
            {
                values.Add(0);
            }
            values[label] += weight;
        }

        public double Get(int label)
        {
            return label < values.Count ? values[label] : 0;
        }

        public static double SmoothStep(double t)
        {
            t = Math.Min(Math.Max(t, 0), 1);
            return t * t * (3 - 2 * t);
        }

        private readonly List<double> values = new List<double>();
    }

    // The field of a with the label for the materials
    public sealed class SdfLabel : SdfNode
    {
        public SdfLabel(SdfNode child, int label)
        {
            this.child = child;
            this.label = label;
        }

        public override double Evaluate(SdfVector p)
        {
            return child.Evaluate(p);
        }

        public override double Lipschitz()
        {
            return child.Lipschitz();
        }

        public override double Lipschitz(SdfVector center, double radius)
        {
            return child.Lipschitz(center, radius);
        }

        public override bool HasLocalBounds()
        {
            return child.HasLocalBounds();
        }

        public override bool IsRigorous()
        {
            return child.IsRigorous();
        }

        public override void Weights(SdfVector p, SdfWeights w, double weight)
        {
            w.Add(label, weight);
        }

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            return context.Compile(child, x, y, z);
        }

        private readonly SdfNode child;
        private readonly int label;
    }

    // Transfer Materials: the distance of the shape and the labels of the other field
    public sealed class SdfTransfer : SdfNode
    {
        public SdfTransfer(SdfNode shape, SdfNode materials)
        {
            this.shape = shape;
            this.materials = materials;
        }

        public override double Evaluate(SdfVector p)
        {
            return shape.Evaluate(p);
        }

        public override double Lipschitz()
        {
            return shape.Lipschitz();
        }

        public override double Lipschitz(SdfVector center, double radius)
        {
            return shape.Lipschitz(center, radius);
        }

        public override bool HasLocalBounds()
        {
            return shape.HasLocalBounds();
        }

        public override bool IsRigorous()
        {
            return shape.IsRigorous();
        }

        public override void Weights(SdfVector p, SdfWeights w, double weight)
        {
            materials.Weights(p, w, weight);
        }

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            return context.Compile(shape, x, y, z);
        }

        private readonly SdfNode shape, materials;
    }

    // Inset: inside b the surface of a is pushed in by depth, with a wall of width edge at the border of b
    public sealed class SdfInset : SdfNode
    {
        public SdfInset(SdfNode a, SdfNode b, double depth, double edge)
        {
            this.a = a;
            this.b = b;
            this.depth = depth;
            this.edge = Math.Max(edge, 1e-9);
        }

        public override double Evaluate(SdfVector p)
        {
            return a.Evaluate(p) + depth * Weight(b.Evaluate(p));
        }

        private double Weight(double distanceToB)
        {
            return SdfWeights.SmoothStep(-distanceToB / edge);
        }

        public override double Lipschitz()
        {
            // The smooth step has slope at most 3/2
            return a.Lipschitz() + Math.Abs(depth) * 1.5 * b.Lipschitz() / edge;
        }

        public override bool IsRigorous()
        {
            return a.IsRigorous() && b.IsRigorous();
        }

        public override void Weights(SdfVector p, SdfWeights w, double weight)
        {
            a.Weights(p, w, weight);
        }

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            Expression va = context.Compile(a, x, y, z), vb = context.Compile(b, x, y, z);
            Expression t = context.Define(context.Clamp(context.Divide(context.Negate(vb), context.Constant(edge)), 0, 1));
            Expression smooth = context.Multiply(context.Multiply(t, t), context.Subtract(context.Constant(3), context.Multiply(context.Constant(2), t)));
            return context.Add(va, context.Multiply(context.Constant(depth), smooth));
        }

        private readonly SdfNode a, b;
        private readonly double depth, edge;
    }

    // Ink: a with the band of the given width around the surface of b removed (an engraved line)
    public sealed class SdfInk : SdfNode
    {
        public SdfInk(SdfNode a, SdfNode b, double width)
        {
            this.a = a;
            this.b = b;
            this.width = width;
        }

        public override double Evaluate(SdfVector p)
        {
            return Math.Max(a.Evaluate(p), 0.5 * width - Math.Abs(b.Evaluate(p)));
        }

        public override double Lipschitz()
        {
            return Math.Max(a.Lipschitz(), b.Lipschitz());
        }

        public override bool IsRigorous()
        {
            return a.IsRigorous() && b.IsRigorous();
        }

        public override void Weights(SdfVector p, SdfWeights w, double weight)
        {
            // The walls of the groove belong to b
            if (a.Evaluate(p) >= 0.5 * width - Math.Abs(b.Evaluate(p)))
            {
                a.Weights(p, w, weight);
            }
            else
            {
                b.Weights(p, w, weight);
            }
        }

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            Expression va = context.Compile(a, x, y, z), vb = context.Compile(b, x, y, z);
            return context.Max(va, context.Subtract(context.Constant(0.5 * width), context.Abs(vb)));
        }

        private readonly SdfNode a, b;
        private readonly double width;
    }

    // Clip: the skin of a (of the given thickness), kept only inside b; the cut leaves the skin open
    public sealed class SdfClip : SdfNode
    {
        public SdfClip(SdfNode a, SdfNode b, double thickness)
        {
            this.a = a;
            this.b = b;
            this.thickness = thickness;
        }

        public override double Evaluate(SdfVector p)
        {
            return Math.Max(Math.Abs(a.Evaluate(p)) - 0.5 * thickness, b.Evaluate(p));
        }

        public override double Lipschitz()
        {
            return Math.Max(a.Lipschitz(), b.Lipschitz());
        }

        public override bool IsRigorous()
        {
            return a.IsRigorous() && b.IsRigorous();
        }

        public override void Weights(SdfVector p, SdfWeights w, double weight)
        {
            a.Weights(p, w, weight);
        }

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            Expression va = context.Compile(a, x, y, z), vb = context.Compile(b, x, y, z);
            return context.Max(context.Subtract(context.Abs(va), context.Constant(0.5 * thickness)), vb);
        }

        private readonly SdfNode a, b;
        private readonly double thickness;
    }

    // The quaternion Julia set: z -> z^2 + c on (x, y, z, slice), with the usual distance estimate 0.5 |z| log |z| / |z'|
    public sealed class SdfJulia : SdfNode
    {
        public SdfJulia(double cx, double cy, double cz, double cw, int iterations, double bailout, double slice, double safety)
        {
            c = new double[] { cx, cy, cz, cw };
            this.iterations = iterations;
            this.bailout = bailout;
            this.slice = slice;
            this.safety = safety;
        }

        public override double Evaluate(SdfVector p)
        {
            double qx = p.x, qy = p.y, qz = p.z, qw = slice;
            double dx = 1, dy = 0, dz = 0, dw = 0;
            double r2 = qx * qx + qy * qy + qz * qz + qw * qw;
            for (int i = 0; i < iterations && r2 < bailout * bailout; ++i)
            {
                // d = 2 q d
                double nx = qx * dx - qy * dy - qz * dz - qw * dw;
                double ny = qx * dy + qy * dx + qz * dw - qw * dz;
                double nz = qx * dz - qy * dw + qz * dx + qw * dy;
                double nw = qx * dw + qy * dz - qz * dy + qw * dx;
                dx = 2 * nx;
                dy = 2 * ny;
                dz = 2 * nz;
                dw = 2 * nw;
                // q = q^2 + c
                double sx = qx * qx - qy * qy - qz * qz - qw * qw + c[0];
                double sy = 2 * qx * qy + c[1];
                double sz = 2 * qx * qz + c[2];
                double sw = 2 * qx * qw + c[3];
                qx = sx;
                qy = sy;
                qz = sz;
                qw = sw;
                r2 = qx * qx + qy * qy + qz * qz + qw * qw;
            }
            double r = Math.Sqrt(r2);
            double dr = Math.Sqrt(dx * dx + dy * dy + dz * dz + dw * dw);
            if (dr == 0 || r == 0)
            {
                return -1e-3;
            }
            double distance = 0.5 * r * Math.Log(r) / dr;
            // Inside (no escape) the iteration leaves r2 below the bailout: the point is in the set
            return r2 < bailout * bailout ? -1e-3 : safety * distance;
        }

        public override bool IsRigorous()
        {
            return false;
        }

        private readonly double[] c;
        private readonly int iterations;
        private readonly double bailout, slice, safety;
    }

    // Metaballs: the blobs add up, the surface is where the sum of their kernels (1 - (d / r)^2)^3 equals the threshold. The field is the
    // difference of the threshold and the sum, divided by the largest possible gradient, which makes it a safe (if short) distance bound.
    public sealed class SdfMetaballs : SdfNode
    {
        // normalized: divide by the gradient of the sum at the point (a first order distance estimate, close to a true distance near the
        // surface, so that a morph with another field is even) instead of by the global bound; the field is then not rigorous
        public SdfMetaballs(double threshold, double[] balls, bool normalized = false)
        {
            this.normalized = normalized;
            this.threshold = threshold;
            this.balls = (double[])balls.Clone();
            count = balls.Length / 5;
            double sum = 0;
            for (int i = 0; i < count; ++i)
            {
                sum += Math.Abs(balls[5 * i + 4]) * 1.7165 / balls[5 * i + 3];
            }
            // The kernel has slope 6 s (1 - s^2)^2 / r, at most 1.7165 / r
            gradientBound = Math.Max(sum, 1e-12);
            double largest = 0;
            for (int i = 0; i < count; ++i)
            {
                largest = Math.Max(largest, balls[5 * i + 3]);
            }
            minimumGradient = Math.Max(threshold / Math.Max(largest, 1e-12), 1e-12);
        }

        public override double Evaluate(SdfVector p)
        {
            double sum = 0, gx = 0, gy = 0, gz = 0;
            for (int i = 0; i < count; ++i)
            {
                double dx = p.x - balls[5 * i], dy = p.y - balls[5 * i + 1], dz = p.z - balls[5 * i + 2];
                double r = balls[5 * i + 3];
                double s2 = (dx * dx + dy * dy + dz * dz) / (r * r);
                if (s2 < 1)
                {
                    double k = 1 - s2;
                    double strength = balls[5 * i + 4];
                    sum += strength * k * k * k;
                    double slope = -6 * strength * k * k / (r * r);
                    gx += slope * dx;
                    gy += slope * dy;
                    gz += slope * dz;
                }
            }
            if (!normalized)
            {
                return (threshold - sum) / gradientBound;
            }
            // Far from the balls the gradient vanishes: the floor keeps the value finite (about the distance to the nearest ball's edge)
            double length = Math.Sqrt(gx * gx + gy * gy + gz * gz);
            return (threshold - sum) / Math.Max(length, minimumGradient);
        }

        public override bool IsRigorous()
        {
            return !normalized;
        }

        private readonly double threshold, gradientBound, minimumGradient;
        private readonly bool normalized;
        private readonly double[] balls;
        private readonly int count;
    }

    // Heights from a texture of the given kind over the projected coordinates
    public sealed class SdfHeight
    {
        public SdfHeight(string kind, string projection, double scale)
        {
            this.projection = projection;
            this.scale = scale;
            switch (kind)
            {
                case "noise":
                case "fbm":
                case "checker":
                case "ripple":
                    this.kind = kind;
                    break;
                default:
                    this.kind = "image";
                    LoadImage(kind);
                    break;
            }
            triplanar = projection == "triplanar";
        }

        private void LoadImage(string filename)
        {
            Image image = Image.Read(filename, null, ColorEncoding.Linear).image;
            width = image.Resolution().x;
            height = image.Resolution().y;
            texels = new float[width * height];
            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    float sum = 0;
                    int channels = Math.Min(image.NChannels(), 3);
                    for (int c = 0; c < channels; ++c)
                    {
                        sum += image.GetChannel(new Point2i(x, height - 1 - y), c);
                    }
                    texels[y * width + x] = sum / channels;
                }
            }
        }

        public bool NeedsNormal()
        {
            return triplanar;
        }

        // The height in [0, 1] (noise may be a little outside) at p; n is the unit normal where the projection needs it
        public double Sample(SdfVector p, SdfVector n)
        {
            if (triplanar)
            {
                double ax = Math.Pow(Math.Abs(n.x), 4), ay = Math.Pow(Math.Abs(n.y), 4), az = Math.Pow(Math.Abs(n.z), 4);
                double total = Math.Max(ax + ay + az, 1e-30);
                return (ax * Lookup(p, "yz") + ay * Lookup(p, "xz") + az * Lookup(p, "xy")) / total;
            }
            if (projection == "box")
            {
                double ax = Math.Abs(p.x), ay = Math.Abs(p.y), az = Math.Abs(p.z);
                return Lookup(p, ax >= ay && ax >= az ? "yz" : (ay >= az ? "xz" : "xy"));
            }
            return Lookup(p, projection);
        }

        private double Lookup(SdfVector p, string projection)
        {
            double u, v;
            switch (projection)
            {
                case "xy":
                    u = p.x;
                    v = p.y;
                    break;
                case "yz":
                    u = p.y;
                    v = p.z;
                    break;
                case "xz":
                    u = p.x;
                    v = p.z;
                    break;
                case "spherical":
                    {
                        double phi = Math.Atan2(p.y, p.x);
                        double r = Math.Max(p.Length(), 1e-30);
                        u = (phi < 0 ? phi + 2 * Math.PI : phi) / (2 * Math.PI);
                        v = Math.Acos(Math.Min(Math.Max(p.z / r, -1), 1)) / Math.PI;
                        break;
                    }
                case "cylindrical":
                    {
                        double phi = Math.Atan2(p.y, p.x);
                        u = (phi < 0 ? phi + 2 * Math.PI : phi) / (2 * Math.PI);
                        v = p.z;
                        break;
                    }
                default:
                    u = p.x;
                    v = p.y;
                    break;
            }
            u *= scale;
            v *= scale;
            switch (kind)
            {
                case "noise":
                    return 0.5 + 0.5 * pbrt.Noise(scale * p.x, scale * p.y, scale * p.z);
                case "fbm":
                    {
                        double sum = 0, weight = 0.5, f = scale;
                        for (int i = 0; i < 6; ++i)
                        {
                            sum += weight * pbrt.Noise(f * p.x, f * p.y, f * p.z);
                            weight *= 0.5;
                            f *= 2;
                        }
                        return 0.5 + 0.5 * sum;
                    }
                case "checker":
                    return (((int)Math.Floor(u) + (int)Math.Floor(v)) & 1) == 0 ? 0 : 1;
                case "ripple":
                    return 0.5 + 0.5 * Math.Sin(2 * Math.PI * Math.Sqrt(u * u + v * v));
                default:
                    return Bilinear(u - Math.Floor(u), v - Math.Floor(v));
            }
        }

        private double Bilinear(double u, double v)
        {
            double fx = u * width - 0.5, fy = v * height - 0.5;
            int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
            double tx = fx - x0, ty = fy - y0;
            int x1 = (x0 + 1 + width) % width, y1 = (y0 + 1 + height) % height;
            x0 = (x0 + width) % width;
            y0 = (y0 + height) % height;
            return (1 - ty) * ((1 - tx) * texels[y0 * width + x0] + tx * texels[y0 * width + x1]) +
                   ty * ((1 - tx) * texels[y1 * width + x0] + tx * texels[y1 * width + x1]);
        }

        private readonly string kind, projection;
        private readonly double scale;
        private readonly bool triplanar;
        private float[] texels;
        private int width, height;
    }

    // Texture displacement of the field: the surface moves along its normal by height * texture + offset (Octane's Vectron texture
    // displacement with its Height, Offset and Step Scale)
    public sealed class SdfTextureDisplace : SdfNode
    {
        public SdfTextureDisplace(SdfNode child, SdfHeight texture, double height, double offset, double stepScale, double domainRadius)
        {
            this.child = child;
            this.texture = texture;
            this.height = height;
            this.offset = offset;
            // The gradient of the texture, measured over a sample of the domain
            double largest = 0;
            RNG rng = new RNG(5);
            const double h = 1e-3;
            for (int i = 0; i < 4000; ++i)
            {
                SdfVector p = new SdfVector((2 * rng.Uniform<Float>() - 1) * domainRadius, (2 * rng.Uniform<Float>() - 1) * domainRadius,
                                            (2 * rng.Uniform<Float>() - 1) * domainRadius);
                SdfVector n = p.Normalized();
                double gx = (texture.Sample(p + new SdfVector(h, 0, 0), n) - texture.Sample(p - new SdfVector(h, 0, 0), n)) / (2 * h);
                double gy = (texture.Sample(p + new SdfVector(0, h, 0), n) - texture.Sample(p - new SdfVector(0, h, 0), n)) / (2 * h);
                double gz = (texture.Sample(p + new SdfVector(0, 0, h), n) - texture.Sample(p - new SdfVector(0, 0, h), n)) / (2 * h);
                largest = Math.Max(largest, Math.Sqrt(gx * gx + gy * gy + gz * gz));
            }
            lipschitz = (child.Lipschitz() + Math.Abs(height) * largest * 1.25) / Math.Max(stepScale, 1e-6);
        }

        public override double Evaluate(SdfVector p)
        {
            SdfVector n = texture.NeedsNormal() ? child.Gradient(p, 1e-4).Normalized() : new SdfVector(0, 0, 1);
            return child.Evaluate(p) - (offset + height * texture.Sample(p, n));
        }

        public override double Lipschitz()
        {
            return lipschitz;
        }

        public override bool IsRigorous()
        {
            // The gradient of the texture is measured, not proven
            return false;
        }

        public override void Weights(SdfVector p, SdfWeights w, double weight)
        {
            child.Weights(p, w, weight);
        }

        private readonly SdfNode child;
        private readonly SdfHeight texture;
        private readonly double height, offset, lipschitz;
    }

    // A mix of two fields by the time of the ray
    public sealed class SdfMorph : SdfNode
    {
        public SdfMorph(SdfNode a, SdfNode b)
        {
            this.a = a;
            this.b = b;
        }

        public override double Evaluate(SdfVector p)
        {
            double t = SdfTime.Clamped();
            if (t <= 0)
            {
                return a.Evaluate(p);
            }
            if (t >= 1)
            {
                return b.Evaluate(p);
            }
            return (1 - t) * a.Evaluate(p) + t * b.Evaluate(p);
        }

        public override double Lipschitz()
        {
            // The weight is constant along a ray
            return Math.Max(a.Lipschitz(), b.Lipschitz());
        }

        public override bool IsRigorous()
        {
            return a.IsRigorous() && b.IsRigorous();
        }

        public override void Weights(SdfVector p, SdfWeights w, double weight)
        {
            double t = SdfTime.Clamped();
            a.Weights(p, w, weight * (1 - t));
            b.Weights(p, w, weight * t);
        }

        private readonly SdfNode a, b;
    }

    // The child moved by v per unit of time
    public sealed class SdfTimeTranslate : SdfNode
    {
        public SdfTimeTranslate(SdfNode child, SdfVector velocity)
        {
            this.child = child;
            this.velocity = velocity;
        }

        public override double Evaluate(SdfVector p)
        {
            return child.Evaluate(p - velocity * SdfTime.Clamped());
        }

        public override double Lipschitz()
        {
            return child.Lipschitz();
        }

        public override bool IsRigorous()
        {
            return child.IsRigorous();
        }

        public override void Weights(SdfVector p, SdfWeights w, double weight)
        {
            child.Weights(p - velocity * SdfTime.Clamped(), w, weight);
        }

        private readonly SdfNode child;
        private readonly SdfVector velocity;
    }

    // The child rotated by (rx, ry, rz) degrees over the unit of time (about x, then y, then z)
    public sealed class SdfTimeRotate : SdfNode
    {
        public SdfTimeRotate(SdfNode child, double rx, double ry, double rz)
        {
            this.child = child;
            this.rx = rx;
            this.ry = ry;
            this.rz = rz;
        }

        private SdfVector Inverse(SdfVector p)
        {
            double t = SdfTime.Clamped() * Math.PI / 180;
            double a = rx * t, b = ry * t, c = rz * t;
            // The inverse of Rz Ry Rx is Rx^-1 Ry^-1 Rz^-1
            double ca = Math.Cos(c), sa = Math.Sin(-c);
            double x1 = ca * p.x - sa * p.y, y1 = sa * p.x + ca * p.y, z1 = p.z;
            double cb = Math.Cos(b), sb = Math.Sin(-b);
            double x2 = cb * x1 + sb * z1, y2 = y1, z2 = -sb * x1 + cb * z1;
            double cc = Math.Cos(a), sc = Math.Sin(-a);
            return new SdfVector(x2, cc * y2 - sc * z2, sc * y2 + cc * z2);
        }

        public override double Evaluate(SdfVector p)
        {
            return child.Evaluate(Inverse(p));
        }

        public override double Lipschitz()
        {
            return child.Lipschitz();
        }

        public override bool IsRigorous()
        {
            return child.IsRigorous();
        }

        public override void Weights(SdfVector p, SdfWeights w, double weight)
        {
            child.Weights(Inverse(p), w, weight);
        }

        private readonly SdfNode child;
        private readonly double rx, ry, rz;
    }

    // Noise displacement whose noise travels through space at the given speed
    public sealed class SdfTimeDisplacement : SdfNode
    {
        public SdfTimeDisplacement(SdfNode child, double amplitude, double frequency, double speed, int octaves, double roughness)
        {
            this.child = child;
            this.amplitude = amplitude;
            this.frequency = frequency;
            this.speed = speed;
            this.octaves = octaves;
            this.roughness = roughness;
            double sum = 0, weight = 1, rate = 1;
            for (int i = 0; i < octaves; ++i)
            {
                sum += weight * rate;
                weight *= roughness;
                rate *= 2;
            }
            lipschitz = child.Lipschitz() + Math.Abs(amplitude) * frequency * SdfNoiseDisplacement.NoiseGradientBound * sum;
        }

        public override double Evaluate(SdfVector p)
        {
            double shift = speed * SdfTime.Clamped();
            double value = 0, weight = 1, scale = frequency;
            for (int i = 0; i < octaves; ++i)
            {
                value += weight * pbrt.Noise(p.x * scale, p.y * scale, p.z * scale + shift * scale);
                weight *= roughness;
                scale *= 2;
            }
            return child.Evaluate(p) + amplitude * value;
        }

        public override double Lipschitz()
        {
            return lipschitz;
        }

        public override bool IsRigorous()
        {
            return child.IsRigorous();
        }

        public override void Weights(SdfVector p, SdfWeights w, double weight)
        {
            child.Weights(p, w, weight);
        }

        private readonly SdfNode child;
        private readonly double amplitude, frequency, speed, roughness, lipschitz;
        private readonly int octaves;
    }

    // A Mandelbulb whose power goes from power0 to power1 over the time (the instances are made for 256 steps of the time)
    public sealed class SdfTimeMandelbulb : SdfNode
    {
        public SdfTimeMandelbulb(double power0, double power1, int iterations, double bailout, double safety)
        {
            steps = new SdfNode[257];
            for (int i = 0; i <= 256; ++i)
            {
                steps[i] = new SdfMandelbulb(power0 + (power1 - power0) * i / 256.0, iterations, bailout, safety);
            }
        }

        public override double Evaluate(SdfVector p)
        {
            return steps[(int)Math.Round(SdfTime.Clamped() * 256)].Evaluate(p);
        }

        public override bool IsRigorous()
        {
            return false;
        }

        private readonly SdfNode[] steps;
    }

    // Texture "sdflabel": the weight (0 to 1) of a labelled operand of the field at the point, to use as the amount of a mix material
    public sealed class SdfLabelTexture : FloatTexture
    {
        public SdfLabelTexture(Transform renderFromTexture, SdfNode field, int label)
        {
            this.renderFromTexture = renderFromTexture;
            this.field = field;
            this.label = label;
        }

        public override Float Evaluate(TextureEvalContext ctx)
        {
            Point3f p = renderFromTexture.ApplyInverse(ctx.p);
            SdfWeights w = new SdfWeights();
            field.Weights(new SdfVector(p.x, p.y, p.z), w, 1);
            return (Float)w.Get(label);
        }

        public override Float MinValue()
        {
            return 0;
        }

        public override Float MaxValue()
        {
            return 1;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ SdfLabelTexture label: %d ]", label);
        }

        public static SdfLabelTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, FileLoc loc,
                                             Allocator alloc)
        {
            string expression = parameters.GetOneString("sdf", "");
            if (expression.Length == 0)
            {
                pbrt.ErrorExit(loc, "sdflabel texture: the \"string sdf\" expression is required.");
            }
            SdfParseContext context = new SdfParseContext();
            context.domainRadius = parameters.GetOneFloat("domainradius", 2);
            context.meshLoader = SdfMeshLoader.Load;
            context.vdbLoader = SdfVdbLoader.Load;
            // Not compiled: the weights are found by walking the tree
            return new SdfLabelTexture(renderFromTexture, SdfParser.Parse(expression, context), parameters.GetOneInt("label", 0));
        }

        private readonly Transform renderFromTexture;
        private readonly SdfNode field;
        private readonly int label;
    }

}  // namespace pbrt
