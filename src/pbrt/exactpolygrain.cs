// (not in pbrt-v4, not in the fork) exactpolygrain.cs
//
// ExactPolyGrainBxDF, material "exactpolygrain": the polydisperse micrograin surface of Xu et al. (2026) and Lucas et al.
// (2024) with every scattering, by tracing the grains themselves. It takes the parameters of "polygrain" (polygrain.cs), whose
// later scatterings are a position-free walk (polygrain_walk.cs); this is another implementation of them, with no walk of
// probabilities.
//
// The surface of the model is a random geometry: hemi-ellipsoidal grains of each type on a base, their centres a Poisson
// process (of the density rho_j = -ln(1 - tau_j) / (pi r_j^2 det M_j) for the type j). A path through it knows its own shape
// (every vertex has its place relative to the first) and only the grains are unknown, and they are independent of each
// other. So the grains are drawn when a ray is traced: a Poisson number of centres around the piece of the ray being
// traced, uniformly placed, each kept if its grain meets the piece, and discarded if it meets a segment that the path has
// already found free (those places are known to be empty; discarding is the thinning of a Poisson process, which leaves the
// process of the grains that are consistent with what the path has seen). The grains that were hit are kept and traced as
// geometry. The nearest hit is the next vertex.
//
// This is a simulation of the model without approximation: each path is a path on a real surface. The BSDF is the mean
// over such surfaces, so it is reciprocal, and with lossless grains and base it loses nothing.
//
// Evaluation, for a direction wi: at a vertex on a diffuse grain or on the bulk, the usual connection, with a shadow ray
// traced through grains drawn the same way. A mirror grain has no BRDF to evaluate, so its part is gathered one step
// earlier: along a ray (o, d) there is for each type one position of a grain that reflects d into wi, the grain whose point
// of the normal n = (wi - d) / |wi - d| lies on the ray; grains of the type are there with the density
//     rho_j dA = rho_j / (4 |d_z| K_j(n)) dwi
// (K_j the Gaussian curvature of the grain at that normal: the ray sweeps |d.n| dA_surface = |d_z| dA_centre, dw_n = K
// dA_surface, dwi = 4 |d.n| dw_n), and the light gets through if the ray reaches that grain and leaves it along wi, which two
// shadow rays through lazily drawn grains answer. For the first vertex this is the closed form of the paper,
//     tauBar0 p_j(n) G~ F / (4 n_z cos_o), with tauBar0 p_j(n) = n_z exp(-Lambda(h)) rho_j / K_j(n),
// and with "bool closedfirst" (the default) the first scattering is that closed form (no noise) and the tracing gives the
// later ones. exactpolygrain_test.cs checks that the traced first scattering is the paper's.
//
// From below (through a bulk that transmits): the point of the base is under a grain with the probability tauBar0, and then
// nothing passes upwards; the bulk reflects downwards as if alone.
//
// A grain that the path has already hit is no longer chance, and a mirror reflection on it has one direction: where the
// vertex before it scatters widely (the bulk, a diffuse grain), the direction from there that the known grain mirrors into wi
// is solved for (Newton's method on the grain) and weighed by the solid angles it relates. Where the vertex before is itself a
// mirror on a grain drawn for it, the direction between the two mirrors is solved for in the same way. Longer chains of
// mirrors that end on a known grain (the vertex before is a mirror on a known grain too) have no such connection: f() and
// PDF() miss those paths (exactpolygrain_test.cs measures the share), and Sample_f, which only traces, has them.
//
// Not here: Mueller matrices; the density of Sample_f is an estimate (as for polygrain).
//
//   integer scatterdepth    the scatterings followed (32)
//   bool closedfirst        the first scattering by the closed form of the paper (true)
//   bool chains             f() and the density include the paths that leave by two or more mirrors on grains already
//                           known, by unbiased manifold sampling (true)
//   and every parameter of "polygrain" but "reciprocal".

using System;
using System.Collections.Generic;

namespace pbrt
{
    public sealed unsafe class ExactPolyGrainBxDF : BxDF
    {
        private struct Grain
        {
            public int type;
            public double cx, cy;
        }

        private struct Segment
        {
            public mg.Vec3d o, d;
            public double length;
        }

        // What a path has found: the grains it hit, and the segments it found free
        private sealed class Field
        {
            public List<Grain> grains = new List<Grain>();
            public List<Segment> free = new List<Segment>();
        }

        public ExactPolyGrainBxDF() { }

        public ExactPolyGrainBxDF(PolyGrainBxDF paper, int maxOrder, bool closedFirst)
        {
            this.paper = paper;
            this.maxOrder = maxOrder;
            this.closedFirst = closedFirst;
            dist = paper.Distribution();
            bulk = paper.Bulk();
            nSamples = paper.Samples();
            for (int j = 0; j < dist.n; ++j)
            {
                rho[j] = dist.a[j] / (Math.PI * Math.Abs(dist.detM[j]));
                // the largest lateral half-axis of the grain: r times the largest eigenvalue of M
                double trace = 0.5 * (dist.M[j, 0] + dist.M[j, 3]);
                double root = Math.Sqrt(Math.Max(0.0, trace * trace - (dist.M[j, 0] * dist.M[j, 3] - dist.M[j, 1] * dist.M[j, 2])));
                reach[j] = dist.r[j] * (Math.Abs(trace) + root);
                top = Math.Max(top, dist.r[j]);
                widest = Math.Max(widest, reach[j]);
            }
            top *= 1 + 1e-9;
        }

        public static string Name() { return "ExactPolyGrainBxDF"; }

        public override string ToString() { return StringPrintf("[ ExactPolyGrainBxDF types: %d maxOrder: %d closedFirst: %s ]", dist.n, maxOrder, closedFirst); }

        public override BxDFFlags Flags() { return paper.Flags(); }

        public override void Regularize() { bulk.Regularize(); }

        // The parameters along the ray o + t d where it enters and leaves the ellipsoid of a grain
        private bool Roots(Grain g, mg.Vec3d o, mg.Vec3d d, out double t0, out double t1)
        {
            int j = g.type;
            double inv = 1 / dist.r[j];
            double px = o.x - g.cx, py = o.y - g.cy;
            double qx = (dist.Minv[j, 0] * px + dist.Minv[j, 1] * py) * inv, qy = (dist.Minv[j, 2] * px + dist.Minv[j, 3] * py) * inv, qz = o.z * inv;
            double ex = (dist.Minv[j, 0] * d.x + dist.Minv[j, 1] * d.y) * inv, ey = (dist.Minv[j, 2] * d.x + dist.Minv[j, 3] * d.y) * inv, ez = d.z * inv;
            double a = ex * ex + ey * ey + ez * ez, b = qx * ex + qy * ey + qz * ez, c = qx * qx + qy * qy + qz * qz - 1;
            double disc = b * b - a * c;
            t0 = t1 = 0;
            if (!(disc > 0) || !(a > 0))
            {
                return false;
            }
            double s = Math.Sqrt(disc);
            t0 = (-b - s) / a;
            t1 = (-b + s) / a;
            return true;
        }

        private bool Touches(Grain g, Segment s)
        {
            double t0, t1;
            return Roots(g, s.o, s.d, out t0, out t1) && t0 <= s.length && t1 >= 0;
        }

        private mg.Vec3d NormalAt(Grain g, mg.Vec3d p)
        {
            int j = g.type;
            double px = p.x - g.cx, py = p.y - g.cy;
            double ux = dist.Minv[j, 0] * px + dist.Minv[j, 1] * py, uy = dist.Minv[j, 2] * px + dist.Minv[j, 3] * py, uz = p.z;
            double nx = dist.Minv[j, 0] * ux + dist.Minv[j, 2] * uy, ny = dist.Minv[j, 1] * ux + dist.Minv[j, 3] * uy, nz = uz;
            double length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            return new mg.Vec3d(nx / length, ny / length, nz / length);
        }

        private static int Poisson(double mean, ref RNG rng)
        {
            int count = 0;
            // (in pieces: exp(-mean) underflows for a large mean)
            while (mean > 0)
            {
                double part = Math.Min(mean, 30.0);
                mean -= part;
                double limit = Math.Exp(-part), product = rng.Uniform<double>();
                while (product > limit)
                {
                    ++count;
                    product *= rng.Uniform<double>();
                }
            }
            return count;
        }

        private const int Escaped = 0, Base = 1, OnGrain = 2, Lost = 3;

        // The nearest grain along the ray from o: among the grains of the field, and among grains drawn now that are consistent
        // with the segments found free. `own` is the grain that the ray leaves (-1: none). With `keep`, a grain drawn and hit
        // joins the field.
        private int Trace(Field field, mg.Vec3d o, mg.Vec3d d, int own, bool keep, ref RNG rng, out double tHit, out int grain)
        {
            grain = -1;
            double tEnd;
            int atEnd;
            if (d.z > 1e-12)
            {
                tEnd = (top - o.z) / d.z;
                atEnd = Escaped;
            }
            else if (d.z < -1e-12)
            {
                tEnd = o.z / -d.z;
                atEnd = Base;
            }
            else
            {
                tEnd = Double.PositiveInfinity;
                atEnd = Lost;
            }
            tHit = tEnd;
            if (!(tEnd > 0))
            {
                tHit = 0;
                return atEnd;
            }
            double best = Double.PositiveInfinity;
            double tiny = 1e-9 * top;
            for (int i = 0; i < field.grains.Count; ++i)
            {
                double t0, t1;
                if (i != own && Roots(field.grains[i], o, d, out t0, out t1) && t0 > tiny && t0 < best)
                {
                    best = t0;
                    grain = i;
                }
            }
            double lateral = Math.Sqrt(d.x * d.x + d.y * d.y);
            double ux = lateral > 1e-12 ? d.x / lateral : 1, uy = lateral > 1e-12 ? d.y / lateral : 0;
            double step = 4 * widest;
            double start = 0;
            Grain drawn = default;
            bool isDrawn = false;
            for (int piece = 0; piece < 100000 && start < Math.Min(tEnd, best); ++piece)
            {
                double end = Math.Min(Math.Min(tEnd, best), start + step);
                double z0 = o.z + start * d.z, z1 = o.z + end * d.z, lowest = Math.Min(z0, z1);
                double length = lateral * (end - start);
                double sx = o.x + start * d.x, sy = o.y + start * d.y;
                for (int j = 0; j < dist.n; ++j)
                {
                    if (lowest >= dist.r[j])
                    {
                        continue;
                    }
                    double R = reach[j];
                    int count = Poisson(rho[j] * (length + 2 * R) * 2 * R, ref rng);
                    for (int c = 0; c < count; ++c)
                    {
                        double along = -R + rng.Uniform<double>() * (length + 2 * R), across = -R + rng.Uniform<double>() * 2 * R;
                        Grain g;
                        g.type = j;
                        g.cx = sx + ux * along - uy * across;
                        g.cy = sy + uy * along + ux * across;
                        double t0, t1;
                        if (!Roots(g, o, d, out t0, out t1) || t1 < 0 || t0 > end || t0 >= best)
                        {
                            continue;
                        }
                        if (t0 < start || t0 <= 0)
                        {
                            continue;  // it meets the part of this ray already found free, or holds its origin
                        }
                        bool consistent = true;
                        for (int s = 0; s < field.free.Count && consistent; ++s)
                        {
                            consistent = !Touches(g, field.free[s]);
                        }
                        if (consistent)
                        {
                            best = t0;
                            drawn = g;
                            isDrawn = true;
                        }
                    }
                }
                start = end;
            }
            if (Double.IsInfinity(best))
            {
                return atEnd == Lost ? Lost : atEnd;
            }
            if (best >= tEnd)
            {
                return atEnd;
            }
            tHit = best;
            if (isDrawn)
            {
                grain = field.grains.Count;
                if (keep)
                {
                    field.grains.Add(drawn);
                }
                else
                {
                    grain = -2;  // a grain that is not kept: the ray is stopped
                }
            }
            return OnGrain;
        }

        private static mg.Vec3d V3(Vector3f v) { return new mg.Vec3d(v.x, v.y, v.z); }
        private static Vector3f F3(mg.Vec3d v) { return new Vector3f((Float)v.x, (Float)v.y, (Float)v.z); }
        private static mg.Vec3d Along(mg.Vec3d o, mg.Vec3d d, double t) { return new mg.Vec3d(o.x + t * d.x, o.y + t * d.y, o.z + t * d.z); }

        // Whether a ray from o leaves the layer upwards (a shadow ray: the grains it draws are not kept)
        private bool Leaves(Field field, mg.Vec3d o, mg.Vec3d d, int own, ref RNG rng)
        {
            if (!(d.z > 0))
            {
                return false;
            }
            double t;
            int grain;
            return Trace(field, o, d, own, false, ref rng, out t, out grain) == Escaped;
        }

        // The direction that a ray from o along d has after the mirror reflection on a grain; false if it misses the grain
        private bool Mirrored(Grain g, mg.Vec3d o, mg.Vec3d d, out mg.Vec3d x, out mg.Vec3d n, out mg.Vec3d r, out double t)
        {
            x = n = r = default;
            double t1;
            if (!Roots(g, o, d, out t, out t1) || !(t > 1e-9 * top))
            {
                return false;
            }
            x = Along(o, d, t);
            if (x.z < 0)
            {
                return false;
            }
            n = NormalAt(g, x);
            double c = d.x * n.x + d.y * n.y + d.z * n.z;
            r = new mg.Vec3d(d.x - 2 * c * n.x, d.y - 2 * c * n.y, d.z - 2 * c * n.z);
            return c < 0;
        }

        private static void Basis(mg.Vec3d w, out mg.Vec3d e1, out mg.Vec3d e2)
        {
            mg.Vec3d a = Math.Abs(w.z) < 0.9 ? new mg.Vec3d(0, 0, 1) : new mg.Vec3d(1, 0, 0);
            e1 = new mg.Vec3d(a.y * w.z - a.z * w.y, a.z * w.x - a.x * w.z, a.x * w.y - a.y * w.x);
            double l = Math.Sqrt(e1.x * e1.x + e1.y * e1.y + e1.z * e1.z);
            e1 = new mg.Vec3d(e1.x / l, e1.y / l, e1.z / l);
            e2 = new mg.Vec3d(w.y * e1.z - w.z * e1.y, w.z * e1.x - w.x * e1.z, w.x * e1.y - w.y * e1.x);
        }

        private static mg.Vec3d Unit(mg.Vec3d v)
        {
            double l = Math.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
            return new mg.Vec3d(v.x / l, v.y / l, v.z / l);
        }

        // A grain that the path already knows is geometry, not chance: from a point o that scatters widely (the bulk, a diffuse
        // grain) there is a direction d whose mirror reflection on that grain is wi. It is found by Newton's method; `jacobian`
        // is the solid angle of reflected directions for a unit solid angle of d there.
        private bool ToKnownMirror(Grain g, mg.Vec3d o, mg.Vec3d wi, out mg.Vec3d d, out mg.Vec3d x, out mg.Vec3d n, out double t, out double jacobian)
        {
            d = x = n = default;
            t = jacobian = 0;
            // The unknown is the normal at the point of reflection, which every direction is a candidate for (the reflected
            // direction as a function of d is only defined where the ray meets the grain, and Newton's method on it leaves
            // that set). The normal there halves the directions to o and to wi; a convex mirror has one such point.
            int j = g.type;
            Grain grain = g;
            wi = Unit(wi);
            mg.Vec3d from = o, to = wi;
            mg.Vec3d[] starts = new mg.Vec3d[] { new mg.Vec3d(wi.x, wi.y, wi.z + 1), new mg.Vec3d(o.x - g.cx + wi.x, o.y - g.cy + wi.y, o.z + wi.z + 0.1),
                                                 new mg.Vec3d(0, 0, 1), new mg.Vec3d(o.x - g.cx, o.y - g.cy, o.z + 0.1) };
            for (int attempt = 0; attempt < starts.Length; ++attempt)
            {
                mg.Vec3d normal = Unit(starts[attempt]);
                if (!GaussNewton((mg.Vec3d m) =>
                    {
                        mg.Vec3d q = PointAt(grain, m);
                        mg.Vec3d back = new mg.Vec3d(from.x - q.x, from.y - q.y, from.z - q.z);
                        double l = Math.Sqrt(back.x * back.x + back.y * back.y + back.z * back.z);
                        if (!(l > 0))
                        {
                            return (mg.Vec3d?)null;
                        }
                        mg.Vec3d half = new mg.Vec3d(back.x / l + to.x, back.y / l + to.y, back.z / l + to.z);
                        double hl = Math.Sqrt(half.x * half.x + half.y * half.y + half.z * half.z);
                        if (!(hl > 1e-9))
                        {
                            return (mg.Vec3d?)null;
                        }
                        return new mg.Vec3d(half.x / hl - m.x, half.y / hl - m.y, half.z / hl - m.z);
                    }, ref normal))
                {
                    continue;
                }
                mg.Vec3d point = PointAt(g, normal);
                if (!(point.z >= 0))
                {
                    return false;
                }
                d = Unit(new mg.Vec3d(point.x - o.x, point.y - o.y, point.z - o.z));
                mg.Vec3d r;
                if (!Mirrored(g, o, d, out x, out n, out r, out t) || !(r.x * wi.x + r.y * wi.y + r.z * wi.z > 1 - 1e-9))
                {
                    return false;
                }
                // (the solid angles: by the reflected direction as a function of d, which is defined around the solution)
                bool solved = Solve((mg.Vec3d direction) =>
                {
                    mg.Vec3d px, pn, pr;
                    double pt;
                    return Mirrored(grain, from, direction, out px, out pn, out pr, out pt) ? pr : (mg.Vec3d?)null;
                }, wi, ref d, out jacobian) && Mirrored(g, o, d, out x, out n, out r, out t);
                return solved;
            }
            return false;
        }

        // The point of a grain where its normal is m
        private mg.Vec3d PointAt(Grain g, mg.Vec3d m)
        {
            int j = g.type;
            mg.Vec3d u;
            dist.MapNormal(j, m, out u);
            return new mg.Vec3d(g.cx + dist.r[j] * (dist.M[j, 0] * u.x + dist.M[j, 1] * u.y), g.cy + dist.r[j] * (dist.M[j, 2] * u.x + dist.M[j, 3] * u.y), dist.r[j] * u.z);
        }

        // The unit vector m at which the three components of `residual` vanish: the Gauss-Newton method on the sphere, with steps
        // shortened until the residual falls
        private static bool GaussNewton(Func<mg.Vec3d, mg.Vec3d?> residual, ref mg.Vec3d m)
        {
            const double h = 1e-6;
            mg.Vec3d? first = residual(m);
            if (first == null)
            {
                return false;
            }
            mg.Vec3d r = first.Value;
            double size = r.x * r.x + r.y * r.y + r.z * r.z;
            int slow = 0;
            for (int step = 0; step < 40; ++step)
            {
                if (size < 1e-22)
                {
                    return true;
                }
                mg.Vec3d e1, e2;
                Basis(m, out e1, out e2);
                mg.Vec3d? pa = residual(Unit(new mg.Vec3d(m.x + h * e1.x, m.y + h * e1.y, m.z + h * e1.z)));
                mg.Vec3d? pb = residual(Unit(new mg.Vec3d(m.x + h * e2.x, m.y + h * e2.y, m.z + h * e2.z)));
                if (pa == null || pb == null)
                {
                    return false;
                }
                mg.Vec3d ja = new mg.Vec3d((pa.Value.x - r.x) / h, (pa.Value.y - r.y) / h, (pa.Value.z - r.z) / h);
                mg.Vec3d jb = new mg.Vec3d((pb.Value.x - r.x) / h, (pb.Value.y - r.y) / h, (pb.Value.z - r.z) / h);
                double aa = ja.x * ja.x + ja.y * ja.y + ja.z * ja.z, ab = ja.x * jb.x + ja.y * jb.y + ja.z * jb.z, bb = jb.x * jb.x + jb.y * jb.y + jb.z * jb.z;
                double ra = ja.x * r.x + ja.y * r.y + ja.z * r.z, rb = jb.x * r.x + jb.y * r.y + jb.z * r.z;
                double det = aa * bb - ab * ab;
                if (!(Math.Abs(det) > 1e-30))
                {
                    return false;
                }
                double da = -(bb * ra - ab * rb) / det, db = -(aa * rb - ab * ra) / det;
                double length = Math.Sqrt(da * da + db * db);
                if (length > 0.5)
                {
                    da *= 0.5 / length;
                    db *= 0.5 / length;
                }
                bool moved = false;
                double sizeBefore = size;
                for (int halving = 0; halving < 8 && !moved; ++halving)
                {
                    mg.Vec3d next = Unit(new mg.Vec3d(m.x + da * e1.x + db * e2.x, m.y + da * e1.y + db * e2.y, m.z + da * e1.z + db * e2.z));
                    mg.Vec3d? at = residual(next);
                    if (at != null)
                    {
                        double s = at.Value.x * at.Value.x + at.Value.y * at.Value.y + at.Value.z * at.Value.z;
                        if (s < size)
                        {
                            m = next;
                            r = at.Value;
                            size = s;
                            moved = true;
                        }
                    }
                    da *= 0.5;
                    db *= 0.5;
                }
                if (!moved)
                {
                    return size < 1e-18;
                }
                // (a residual that stops falling far from zero is at a minimum that is not a root)
                slow = size > 0.7 * sizeBefore ? slow + 1 : 0;
                if (slow >= 4 && size > 1e-12)
                {
                    return false;
                }
            }
            return size < 1e-18;
        }

        // Newton's method for the direction d that `map` sends to wi (a mirror reflection, or two); `jacobian` is the solid
        // angle around wi for a unit solid angle around d
        private static bool Solve(Func<mg.Vec3d, mg.Vec3d?> map, mg.Vec3d wi, ref mg.Vec3d d, out double jacobian)
        {
            jacobian = 0;
            mg.Vec3d f1, f2;
            Basis(wi, out f1, out f2);
            const double h = 1e-6;
            for (int step = 0; step < 30; ++step)
            {
                mg.Vec3d? at = map(d);
                if (at == null)
                {
                    return false;
                }
                mg.Vec3d r = at.Value;
                double r1 = r.x * f1.x + r.y * f1.y + r.z * f1.z, r2 = r.x * f2.x + r.y * f2.y + r.z * f2.z;
                mg.Vec3d e1, e2;
                Basis(d, out e1, out e2);
                mg.Vec3d? pa = map(Unit(new mg.Vec3d(d.x + h * e1.x, d.y + h * e1.y, d.z + h * e1.z)));
                mg.Vec3d? pb = map(Unit(new mg.Vec3d(d.x + h * e2.x, d.y + h * e2.y, d.z + h * e2.z)));
                if (pa == null || pb == null)
                {
                    return false;
                }
                mg.Vec3d ra = pa.Value, rb = pb.Value;
                double a11 = ((ra.x * f1.x + ra.y * f1.y + ra.z * f1.z) - r1) / h, a12 = ((rb.x * f1.x + rb.y * f1.y + rb.z * f1.z) - r1) / h;
                double a21 = ((ra.x * f2.x + ra.y * f2.y + ra.z * f2.z) - r2) / h, a22 = ((rb.x * f2.x + rb.y * f2.y + rb.z * f2.z) - r2) / h;
                double det = a11 * a22 - a12 * a21;
                if (!(Math.Abs(det) > 1e-12))
                {
                    return false;
                }
                double along = r.x * wi.x + r.y * wi.y + r.z * wi.z;
                if (r1 * r1 + r2 * r2 < 1e-18 && along > 0)
                {
                    jacobian = Math.Abs(det) * along;
                    return true;
                }
                double da = -(a22 * r1 - a12 * r2) / det, db = -(-a21 * r1 + a11 * r2) / det;
                double size = Math.Sqrt(da * da + db * db);
                if (size > 0.3)
                {
                    da *= 0.3 / size;
                    db *= 0.3 / size;
                }
                // (a step that leaves the grain is halved until it does not)
                mg.Vec3d next = d;
                bool moved = false;
                for (int halving = 0; halving < 10 && !moved; ++halving)
                {
                    next = Unit(new mg.Vec3d(d.x + da * e1.x + db * e2.x, d.y + da * e1.y + db * e2.y, d.z + da * e1.z + db * e2.z));
                    moved = map(next) != null;
                    da *= 0.5;
                    db *= 0.5;
                }
                if (!moved)
                {
                    return false;
                }
                d = next;
            }
            return false;
        }

        // The grain of the type j that mirrors the ray (o, d) into the direction `to`: its point on the ray and itself
        private bool FreshMirror(int j, mg.Vec3d o, mg.Vec3d d, mg.Vec3d to, out Grain g, out mg.Vec3d hit, out mg.Vec3d n, out double t, out double curvature)
        {
            g = default;
            hit = default;
            t = curvature = 0;
            n = new mg.Vec3d(to.x - d.x, to.y - d.y, to.z - d.z);
            double norm = Math.Sqrt(n.x * n.x + n.y * n.y + n.z * n.z);
            if (!(norm > 1e-9))
            {
                return false;
            }
            n = new mg.Vec3d(n.x / norm, n.y / norm, n.z / norm);
            if (!(n.z > 0))
            {
                return false;
            }
            mg.Vec3d u;
            double len = dist.MapNormal(j, n, out u);
            t = (dist.r[j] * u.z - o.z) / d.z;
            if (!(t > 1e-9 * top))
            {
                return false;
            }
            hit = Along(o, d, t);
            g.type = j;
            g.cx = hit.x - dist.r[j] * (dist.M[j, 0] * u.x + dist.M[j, 1] * u.y);
            g.cy = hit.y - dist.r[j] * (dist.M[j, 2] * u.x + dist.M[j, 3] * u.y);
            curvature = len * len * len * len / (dist.r[j] * dist.r[j] * dist.detM[j] * dist.detM[j]);
            return true;
        }

        // The directions between a grain of the type j drawn on the ray (o, d) and the known grain `second`, such that the two
        // mirror reflections end in wi. The unknown is the normal of the known grain at its reflection: it fixes the point there
        // and the direction that arrives, so the normal of the drawn grain, so the height at which the ray meets that grain; the
        // line back from the known grain has to pass through that point of the ray. There can be more than one.
        private int TwoMirrors(Grain second, int j, mg.Vec3d o, mg.Vec3d d, mg.Vec3d wi, mg.Vec3d[] between, double[] jacobian)
        {
            wi = Unit(wi);
            d = Unit(d);
            int found = 0;
            int type = j;
            mg.Vec3d from = o, along = d, to = wi;
            Grain known = second;
            Func<mg.Vec3d, mg.Vec3d?> residual = (mg.Vec3d m) =>
            {
                mg.Vec3d x2 = PointAt(known, m);
                double c = m.x * to.x + m.y * to.y + m.z * to.z;
                mg.Vec3d b = new mg.Vec3d(to.x - 2 * c * m.x, to.y - 2 * c * m.y, to.z - 2 * c * m.z);
                mg.Vec3d n1 = new mg.Vec3d(b.x - along.x, b.y - along.y, b.z - along.z);
                double norm = Math.Sqrt(n1.x * n1.x + n1.y * n1.y + n1.z * n1.z);
                if (!(norm > 1e-9))
                {
                    return (mg.Vec3d?)null;
                }
                mg.Vec3d u;
                dist.MapNormal(type, new mg.Vec3d(n1.x / norm, n1.y / norm, n1.z / norm), out u);
                double t = (dist.r[type] * u.z - from.z) / along.z;
                mg.Vec3d v = new mg.Vec3d(x2.x - from.x - t * along.x, x2.y - from.y - t * along.y, x2.z - from.z - t * along.z);
                double vb = v.x * b.x + v.y * b.y + v.z * b.z;
                return new mg.Vec3d((v.x - vb * b.x) / top, (v.y - vb * b.y) / top, (v.z - vb * b.z) / top);
            };
            // first guesses: the normals that would mirror into wi the light of points of the ray, and a few spread ones
            mg.Vec3d[] starts = new mg.Vec3d[18];
            int nStarts = 0;
            for (int k = 0; k < 4; ++k)
            {
                double height = dist.r[j] * (k == 3 ? 0.98 : 0.33 * k);
                double t = (height - o.z) / d.z;
                mg.Vec3d point = t > 0 ? Along(o, d, t) : o, direction, x, n;
                double tt, unused;
                if (ToKnownMirror(second, point, wi, out direction, out x, out n, out tt, out unused))
                {
                    starts[nStarts++] = n;
                }
            }
            mg.Vec3d e1, e2;
            Basis(wi, out e1, out e2);
            starts[nStarts++] = Unit(new mg.Vec3d(wi.x, wi.y, wi.z + 1));
            starts[nStarts++] = Unit(new mg.Vec3d(wi.x + 0.7 * e1.x, wi.y + 0.7 * e1.y, wi.z + 0.7 * e1.z));
            starts[nStarts++] = Unit(new mg.Vec3d(wi.x - 0.7 * e1.x, wi.y - 0.7 * e1.y, wi.z - 0.7 * e1.z));
            starts[nStarts++] = Unit(new mg.Vec3d(wi.x + 0.7 * e2.x, wi.y + 0.7 * e2.y, wi.z + 0.7 * e2.z));
            starts[nStarts++] = Unit(new mg.Vec3d(wi.x - 0.7 * e2.x, wi.y - 0.7 * e2.y, wi.z - 0.7 * e2.z));
            starts[nStarts++] = new mg.Vec3d(0, 0, 1);
            for (int k = 0; k < 8; ++k)
            {
                double angle = 0.25 * Math.PI * k, z = (k & 1) == 0 ? 0.3 : 0.75, s = Math.Sqrt(1 - z * z);
                starts[nStarts++] = new mg.Vec3d(s * Math.Cos(angle), s * Math.Sin(angle), z);
            }
            for (int s = 0; s < nStarts && found < between.Length; ++s)
            {
                mg.Vec3d m = starts[s];
                if (!GaussNewton(residual, ref m))
                {
                    continue;
                }
                double c = m.x * wi.x + m.y * wi.y + m.z * wi.z;
                if (!(c > 0))
                {
                    continue;
                }
                mg.Vec3d b = Unit(new mg.Vec3d(wi.x - 2 * c * m.x, wi.y - 2 * c * m.y, wi.z - 2 * c * m.z));
                bool isNew = true;
                for (int k = 0; k < found && isNew; ++k)
                {
                    isNew = between[k].x * b.x + between[k].y * b.y + between[k].z * b.z < 1 - 1e-10;
                }
                if (!isNew)
                {
                    continue;
                }
                double jac;
                if (!Solve((mg.Vec3d direction) =>
                    {
                        Grain a;
                        mg.Vec3d ha, na, px, pn, pr;
                        double ta, ka, pt;
                        if (!FreshMirror(type, from, along, direction, out a, out ha, out na, out ta, out ka) || !Mirrored(known, ha, direction, out px, out pn, out pr, out pt))
                        {
                            return (mg.Vec3d?)null;
                        }
                        return pr;
                    }, wi, ref b, out jac))
                {
                    continue;
                }
                isNew = true;
                for (int k = 0; k < found && isNew; ++k)
                {
                    isNew = between[k].x * b.x + between[k].y * b.y + between[k].z * b.z < 1 - 1e-10;
                }
                if (isNew)
                {
                    between[found] = b;
                    jacobian[found] = jac;
                    ++found;
                }
            }
            return found;
        }

        // A grain drawn on the ray (o, d) that mirrors it onto a mirror grain that the path already knows, which mirrors it
        // into wi: the direction between the two is solved for
        private SampledSpectrum FreshThenKnown(Field field, mg.Vec3d o, mg.Vec3d d, int own, mg.Vec3d wi, bool unit, ref RNG rng)
        {
            SampledSpectrum total = new SampledSpectrum(0f);
            int known = field.grains.Count;
            for (int i = 0; i < known; ++i)
            {
                Grain second = field.grains[i];
                if (!paper.GrainIsSpecular(second.type))
                {
                    continue;
                }
                for (int j = 0; j < dist.n; ++j)
                {
                    if (!paper.GrainIsSpecular(j))
                    {
                        continue;
                    }
                    mg.Vec3d[] directions = new mg.Vec3d[4];
                    double[] jacobians = new double[4];
                    int roots = TwoMirrors(second, j, o, d, wi, directions, jacobians);
                    for (int root = 0; root < roots; ++root)
                    {
                        mg.Vec3d between = directions[root], x2, n2;
                        double t2, jacobian = jacobians[root];
                        Grain first;
                        mg.Vec3d hit, n1, r;
                        double t1, curvature;
                        if (!FreshMirror(j, o, d, between, out first, out hit, out n1, out t1, out curvature) || !Mirrored(second, hit, between, out x2, out n2, out r, out t2) ||
                            !(wi.x * n2.x + wi.y * n2.y + wi.z * n2.z > 0))
                        {
                            continue;
                        }
                        bool consistent = true;
                        for (int s = 0; s < field.free.Count && consistent; ++s)
                        {
                            consistent = !Touches(first, field.free[s]);
                        }
                        if (!consistent)
                        {
                            continue;
                        }
                        int index = field.grains.Count;
                        field.grains.Add(first);
                        int pushed = 0;
                        double tFound;
                        int found;
                        bool through = Trace(field, o, d, own, false, ref rng, out tFound, out found) == OnGrain && found == index;
                        if (through)
                        {
                            Segment s1;
                            s1.o = o;
                            s1.d = d;
                            s1.length = t1;
                            field.free.Add(s1);
                            ++pushed;
                            through = Trace(field, hit, between, index, false, ref rng, out tFound, out found) == OnGrain && found == i;
                        }
                        if (through)
                        {
                            Segment s2;
                            s2.o = hit;
                            s2.d = between;
                            s2.length = t2;
                            field.free.Add(s2);
                            ++pushed;
                            through = Leaves(field, x2, wi, i, ref rng);
                        }
                        field.free.RemoveRange(field.free.Count - pushed, pushed);
                        field.grains.RemoveAt(index);
                        if (!through)
                        {
                            continue;
                        }
                        SampledSpectrum f1, f2;
                        Float p1 = paper.GrainSpecularProbability(j, (Float)Math.Abs(d.x * n1.x + d.y * n1.y + d.z * n1.z), out f1);
                        Float p2 = paper.GrainSpecularProbability(second.type, (Float)Math.Abs(between.x * n2.x + between.y * n2.y + between.z * n2.z), out f2);
                        Float density = (Float)(rho[j] / (4 * Math.Abs(d.z) * curvature * jacobian));
                        total += unit ? new SampledSpectrum(density * p1 * p2) : f1 * f2 * density;
                    }
                }
            }
            return total;
        }

        // What a point that scatters widely sends to wi by one more reflection, on a mirror grain that the path already knows.
        // `scatter` gives the value of the scattering at the point towards a direction, times the cosine there (or, for the
        // density, the density of that direction).
        private SampledSpectrum ViaKnownMirrors(Field field, mg.Vec3d o, int own, mg.Vec3d wi, bool unit, Func<mg.Vec3d, SampledSpectrum> scatter, ref RNG rng)
        {
            SampledSpectrum total = new SampledSpectrum(0f);
            int known = field.grains.Count;
            for (int i = 0; i < known; ++i)
            {
                if (i == own || !paper.GrainIsSpecular(field.grains[i].type))
                {
                    continue;
                }
                mg.Vec3d d, x, n;
                double t, jacobian;
                if (!ToKnownMirror(field.grains[i], o, wi, out d, out x, out n, out t, out jacobian) || !(wi.x * n.x + wi.y * n.y + wi.z * n.z > 0))
                {
                    continue;
                }
                SampledSpectrum value = scatter(d);
                if (!(bool)value)
                {
                    continue;
                }
                double tFirst;
                int first;
                if (Trace(field, o, d, own, false, ref rng, out tFirst, out first) != OnGrain || first != i)
                {
                    continue;
                }
                Segment to;
                to.o = o;
                to.d = d;
                to.length = t;
                field.free.Add(to);
                bool through = Leaves(field, x, wi, i, ref rng);
                field.free.RemoveAt(field.free.Count - 1);
                if (!through)
                {
                    continue;
                }
                SampledSpectrum fresnel;
                Float pSpecular = paper.GrainSpecularProbability(field.grains[i].type, (Float)Math.Abs(d.x * n.x + d.y * n.y + d.z * n.z), out fresnel);
                total += unit ? value * (pSpecular / (Float)jacobian) : value * fresnel / (Float)jacobian;
            }
            return total;
        }

        // ---- Chains of mirrors on known grains (two or more), by the unbiased manifold sampling of Zeltner et al. 2020 ----
        // After the last event that has chance in it (a wide scattering, or a mirror on a grain drawn for it) a path may leave
        // by any number of mirror reflections on grains it already knows. That is geometry: the directions that do so are the
        // roots of an equation, and there is no list of them to go through. A trial starts Newton's method from a random
        // direction towards a known mirror grain; it finds a root k with some probability p_k, or none. The value of the root
        // found is divided by p_k, and 1 / p_k is estimated without bias by the number of further trials until the same root
        // is found again. Every root is found with a positive probability, so the expectation is the sum over all of them.

        private const int MaxChain = 8, MaxTrials = 262144;

        // The mirror grains that a ray meets one after another among the grains known, and the direction it has after them
        private int Sequence(Field field, mg.Vec3d o, mg.Vec3d d, int own, int[] sequence, out mg.Vec3d exit)
        {
            int count = 0, current = own;
            double tiny = 1e-9 * top;
            exit = d;
            while (true)
            {
                int nearest = -1;
                double best = Double.PositiveInfinity;
                for (int i = 0; i < field.grains.Count; ++i)
                {
                    double t0, t1;
                    if (i != current && paper.GrainIsSpecular(field.grains[i].type) && Roots(field.grains[i], o, d, out t0, out t1) && t0 > tiny && t0 < best)
                    {
                        best = t0;
                        nearest = i;
                    }
                }
                if (nearest < 0)
                {
                    break;
                }
                mg.Vec3d x, n, r;
                double t;
                if (count == sequence.Length || !Mirrored(field.grains[nearest], o, d, out x, out n, out r, out t))
                {
                    return -1;
                }
                sequence[count++] = nearest;
                o = x;
                d = r;
                current = nearest;
            }
            exit = d;
            return count;
        }

        // The direction after the mirror reflections on the grains of a sequence, in its order
        private bool Through(Field field, int[] sequence, int count, mg.Vec3d o, mg.Vec3d d, out mg.Vec3d exit)
        {
            exit = d;
            for (int k = 0; k < count; ++k)
            {
                mg.Vec3d x, n, r;
                double t;
                if (!Mirrored(field.grains[sequence[k]], o, d, out x, out n, out r, out t))
                {
                    return false;
                }
                o = x;
                d = r;
            }
            exit = d;
            return true;
        }

        private static bool SameSequence(int[] a, int na, int[] b, int nb)
        {
            if (na != nb)
            {
                return false;
            }
            for (int k = 0; k < na; ++k)
            {
                if (a[k] != b[k])
                {
                    return false;
                }
            }
            return true;
        }

        // A point at random in the ball around a known grain that can mirror (false: there are not `needed` such grains)
        private bool Target(Field field, int excluded, int needed, ref RNG rng, out mg.Vec3d target)
        {
            target = default;
            int mirrors = 0, candidates = 0;
            for (int i = 0; i < field.grains.Count; ++i)
            {
                if (paper.GrainIsSpecular(field.grains[i].type))
                {
                    ++mirrors;
                    candidates += i != excluded ? 1 : 0;
                }
            }
            if (mirrors < needed || candidates == 0)
            {
                return false;
            }
            int pick = Math.Min((int)(rng.Uniform<double>() * candidates), candidates - 1);
            for (int i = 0; i < field.grains.Count; ++i)
            {
                if (i == excluded || !paper.GrainIsSpecular(field.grains[i].type))
                {
                    continue;
                }
                if (pick-- == 0)
                {
                    Grain g = field.grains[i];
                    double radius = Math.Max(reach[g.type], dist.r[g.type]) * Math.Pow(rng.Uniform<double>(), 1.0 / 3.0);
                    mg.Vec3d w = V3(SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>())));
                    target = new mg.Vec3d(g.cx + radius * w.x, g.cy + radius * w.y, Math.Abs(radius * w.z));
                    return true;
                }
            }
            return false;
        }

        // One trial from a point that scatters widely: a direction whose reflections on two or more known grains end in wi
        private bool WideTrial(Field field, mg.Vec3d o, int own, mg.Vec3d wi, ref RNG rng, int[] sequence, out int count, out mg.Vec3d d, out double jacobian)
        {
            count = 0;
            d = default;
            jacobian = 0;
            mg.Vec3d target, exit;
            if (!Target(field, own, 2, ref rng, out target))
            {
                return false;
            }
            mg.Vec3d offset = new mg.Vec3d(target.x - o.x, target.y - o.y, target.z - o.z);
            if (!(offset.x * offset.x + offset.y * offset.y + offset.z * offset.z > 0))
            {
                return false;
            }
            d = Unit(offset);
            count = Sequence(field, o, d, own, sequence, out exit);
            if (count < 2)
            {
                return false;
            }
            int length = count;
            mg.Vec3d from = o;
            if (!Solve((mg.Vec3d direction) =>
                {
                    mg.Vec3d r;
                    return Through(field, sequence, length, from, direction, out r) ? r : (mg.Vec3d?)null;
                }, wi, ref d, out jacobian))
            {
                return false;
            }
            // (the root has to be a path: the grains of the sequence are the ones met, in that order)
            int[] met = new int[MaxChain];
            return SameSequence(met, Sequence(field, o, d, own, met, out exit), sequence, count);
        }

        // One trial from a ray: a grain of the type j drawn on it that mirrors it into wi by two or more further mirrors, on
        // known grains or on itself again (two grains that face each other). The drawn grain is in the field while the trial
        // lasts, at the index that the sequence names it by; it moves with the direction that is solved for.
        private bool FreshTrial(Field field, int j, mg.Vec3d o, mg.Vec3d d, mg.Vec3d wi, ref RNG rng, int[] sequence, out int count, out mg.Vec3d between,
                                out double jacobian)
        {
            count = 0;
            between = default;
            jacobian = 0;
            mg.Vec3d target, exit;
            if (!Target(field, -1, 1, ref rng, out target))
            {
                return false;
            }
            double ta = -o.z / d.z, tb = (dist.r[j] - o.z) / d.z;
            double lo = Math.Max(Math.Min(ta, tb), 0), hi = Math.Max(ta, tb);
            if (!(hi > lo))
            {
                return false;
            }
            mg.Vec3d point = Along(o, d, lo + rng.Uniform<double>() * (hi - lo));
            mg.Vec3d offset = new mg.Vec3d(target.x - point.x, target.y - point.y, target.z - point.z);
            if (!(offset.x * offset.x + offset.y * offset.y + offset.z * offset.z > 0))
            {
                return false;
            }
            between = Unit(offset);
            Grain first;
            mg.Vec3d hit, n1;
            double t1, curvature;
            if (!FreshMirror(j, o, d, between, out first, out hit, out n1, out t1, out curvature))
            {
                return false;
            }
            int index = field.grains.Count;
            field.grains.Add(first);
            bool found = false;
            count = Sequence(field, hit, between, index, sequence, out exit);
            if (count >= 2)
            {
                int length = count, type = j;
                mg.Vec3d from = o, along = d;
                found = Solve((mg.Vec3d direction) =>
                {
                    Grain a;
                    mg.Vec3d ha, na, r;
                    double ta2, ka;
                    if (!FreshMirror(type, from, along, direction, out a, out ha, out na, out ta2, out ka))
                    {
                        return (mg.Vec3d?)null;
                    }
                    field.grains[index] = a;
                    return Through(field, sequence, length, ha, direction, out r) ? r : (mg.Vec3d?)null;
                }, wi, ref between, out jacobian) && FreshMirror(j, o, d, between, out first, out hit, out n1, out t1, out curvature);
                if (found)
                {
                    field.grains[index] = first;
                    int[] met = new int[MaxChain];
                    found = SameSequence(met, Sequence(field, hit, between, index, met, out exit), sequence, count);
                }
            }
            field.grains.RemoveAt(index);
            return found;
        }

        // Whether the ray from o reaches the grains of the sequence one after another and then leaves along wi (grains drawn on
        // the way stop it), and the product of the reflectances
        private bool Follow(Field field, mg.Vec3d o, mg.Vec3d d, int own, int[] sequence, int count, mg.Vec3d wi, bool unit, ref RNG rng, out SampledSpectrum weight)
        {
            weight = new SampledSpectrum(1f);
            int pushed = 0;
            bool through = true;
            mg.Vec3d n = default;
            for (int k = 0; k < count && through; ++k)
            {
                int i = sequence[k];
                double tFound, t = 0;
                int found;
                mg.Vec3d x = default, r = default;
                through = Trace(field, o, d, own, false, ref rng, out tFound, out found) == OnGrain && found == i && Mirrored(field.grains[i], o, d, out x, out n, out r, out t);
                if (!through)
                {
                    break;
                }
                Segment s;
                s.o = o;
                s.d = d;
                s.length = t;
                field.free.Add(s);
                ++pushed;
                SampledSpectrum fresnel;
                Float pSpecular = paper.GrainSpecularProbability(field.grains[i].type, (Float)Math.Abs(d.x * n.x + d.y * n.y + d.z * n.z), out fresnel);
                weight *= unit ? new SampledSpectrum(pSpecular) : fresnel;
                o = x;
                d = r;
                own = i;
            }
            through = through && wi.x * n.x + wi.y * n.y + wi.z * n.z > 0 && Leaves(field, o, wi, own, ref rng);
            field.free.RemoveRange(field.free.Count - pushed, pushed);
            return through;
        }

        // What a point that scatters widely sends to wi by two or more mirrors on known grains
        private SampledSpectrum ChainsFromWide(Field field, mg.Vec3d o, int own, mg.Vec3d wi, bool unit, Func<mg.Vec3d, SampledSpectrum> scatter, ref RNG rng)
        {
            SampledSpectrum none = new SampledSpectrum(0f);
            int[] sequence = new int[MaxChain], again = new int[MaxChain];
            int count, countAgain;
            mg.Vec3d d, dAgain;
            double jacobian, unused;
            if (!WideTrial(field, o, own, wi, ref rng, sequence, out count, out d, out jacobian))
            {
                return none;
            }
            int trials = 1;
            while (trials <= MaxTrials && !(WideTrial(field, o, own, wi, ref rng, again, out countAgain, out dAgain, out unused) && SameSequence(again, countAgain, sequence, count) &&
                                           d.x * dAgain.x + d.y * dAgain.y + d.z * dAgain.z > 1 - 1e-10))
            {
                ++trials;
            }
            if (trials > MaxTrials)
            {
                return none;
            }
            SampledSpectrum value = scatter(d), weight;
            if (!(bool)value || !Follow(field, o, d, own, sequence, count, wi, unit, ref rng, out weight))
            {
                return none;
            }
            return value * weight * (Float)(trials / jacobian);
        }

        // What a ray sends to wi by a mirror on a grain drawn on it and then two or more mirrors on known grains
        private SampledSpectrum ChainsFromFresh(Field field, mg.Vec3d o, mg.Vec3d d, int own, mg.Vec3d wi, bool unit, ref RNG rng)
        {
            SampledSpectrum total = new SampledSpectrum(0f);
            int[] sequence = new int[MaxChain], again = new int[MaxChain];
            for (int j = 0; j < dist.n; ++j)
            {
                if (!paper.GrainIsSpecular(j))
                {
                    continue;
                }
                int count, countAgain;
                mg.Vec3d between, betweenAgain;
                double jacobian, unused;
                if (!FreshTrial(field, j, o, d, wi, ref rng, sequence, out count, out between, out jacobian))
                {
                    continue;
                }
                int trials = 1;
                while (trials <= MaxTrials && !(FreshTrial(field, j, o, d, wi, ref rng, again, out countAgain, out betweenAgain, out unused) &&
                                               SameSequence(again, countAgain, sequence, count) &&
                                               between.x * betweenAgain.x + between.y * betweenAgain.y + between.z * betweenAgain.z > 1 - 1e-10))
                {
                    ++trials;
                }
                if (trials > MaxTrials)
                {
                    continue;
                }
                Grain first;
                mg.Vec3d hit, n1;
                double t1, curvature;
                if (!FreshMirror(j, o, d, between, out first, out hit, out n1, out t1, out curvature))
                {
                    continue;
                }
                bool consistent = true;
                for (int s = 0; s < field.free.Count && consistent; ++s)
                {
                    consistent = !Touches(first, field.free[s]);
                }
                if (!consistent)
                {
                    continue;
                }
                int index = field.grains.Count;
                field.grains.Add(first);
                double tFound;
                int found;
                SampledSpectrum weight = new SampledSpectrum(0f);
                bool through = Trace(field, o, d, own, false, ref rng, out tFound, out found) == OnGrain && found == index;
                if (through)
                {
                    Segment s1;
                    s1.o = o;
                    s1.d = d;
                    s1.length = t1;
                    field.free.Add(s1);
                    through = Follow(field, hit, between, index, sequence, count, wi, unit, ref rng, out weight);
                    field.free.RemoveAt(field.free.Count - 1);
                }
                field.grains.RemoveAt(index);
                if (!through)
                {
                    continue;
                }
                SampledSpectrum f1;
                Float p1 = paper.GrainSpecularProbability(j, (Float)Math.Abs(d.x * n1.x + d.y * n1.y + d.z * n1.z), out f1);
                Float density = (Float)(trials * rho[j] / (4 * Math.Abs(d.z) * curvature * jacobian));
                total += unit ? weight * (density * p1) : f1 * weight * density;
            }
            return total;
        }

        // (for the tests) How often the solvers find again a reflection that was made by tracing: on a known mirror grain from a
        // point, and on a drawn grain and then the known one
        public void SolverRecovers(int type, int count, ref RNG rng, out int single, out int singleFound, out int twice, out int twiceFound)
        {
            single = singleFound = twice = twiceFound = 0;
            Grain g;
            g.type = type;
            g.cx = 0;
            g.cy = 0;
            double R = reach[type];
            for (int i = 0; i < count; ++i)
            {
                double angle = 2 * Math.PI * rng.Uniform<double>(), radius = 3 * R * Math.Sqrt(rng.Uniform<double>());
                mg.Vec3d o = new mg.Vec3d(radius * Math.Cos(angle), radius * Math.Sin(angle), top * rng.Uniform<double>() * (rng.Uniform<double>() < 0.5 ? 0 : 1));
                mg.Vec3d d = Unit(V3(SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()))));
                mg.Vec3d x, n, r, d2;
                double t, jacobian;
                if (Mirrored(g, o, d, out x, out n, out r, out t) && r.z > 0.05)
                {
                    ++single;
                    if (ToKnownMirror(g, o, r, out d2, out x, out n, out t, out jacobian) && d2.x * d.x + d2.y * d.y + d2.z * d.z > 1 - 1e-8)
                    {
                        ++singleFound;
                    }
                }
                // a ray, a direction between, the drawn grain that makes it, the known grain after it
                mg.Vec3d between = Unit(V3(SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()))));
                Grain first;
                mg.Vec3d hit, n1;
                double t1, curvature;
                if (Math.Abs(d.z) > 1e-3 && FreshMirror(type, o, d, between, out first, out hit, out n1, out t1, out curvature) && hit.z < top && hit.z > 0 &&
                    Mirrored(g, hit, between, out x, out n, out r, out t) && r.z > 0.05)
                {
                    ++twice;
                    mg.Vec3d[] found = new mg.Vec3d[4];
                    double[] jacobians = new double[4];
                    int roots = TwoMirrors(g, type, o, d, r, found, jacobians);
                    bool among = false;
                    for (int k = 0; k < roots; ++k)
                    {
                        among |= found[k].x * between.x + found[k].y * between.y + found[k].z * between.z > 1 - 1e-8;
                    }
                    if (among)
                    {
                        ++twiceFound;
                    }
                }
            }
        }

        // (for the tests) From the point o of the base, lit evenly: the cosine-weighted density of the directions that the grain
        // of the type and centre given mirrors into wi, as the connection has it (-1: none), and by tracing: the directions
        // that `count` cosine-distributed rays are mirrored into, counted in the cone of the half angle given around wi
        public double KnownMirrorDensity(int type, double cx, double cy, Vector3f from, Vector3f wi, int count, double halfAngle, ref RNG rng, out double traced)
        {
            Grain g;
            g.type = type;
            g.cx = cx;
            g.cy = cy;
            mg.Vec3d o = V3(from), to = V3(wi), d, x, n, r;
            double t, jacobian;
            double cosCone = Math.Cos(halfAngle), inCone = 0;
            for (int i = 0; i < count; ++i)
            {
                Vector3f w = SampleCosineHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                if (Mirrored(g, o, V3(w), out x, out n, out r, out t) && r.x * to.x + r.y * to.y + r.z * to.z > cosCone)
                {
                    inCone += 1;
                }
            }
            traced = inCone / count / (2 * Math.PI * (1 - cosCone));
            if (!ToKnownMirror(g, o, to, out d, out x, out n, out t, out jacobian))
            {
                return -1;
            }
            return d.z / Math.PI / jacobian;
        }

        // One path from the direction wo. With `evaluate`, what it sends to wi is added to `sum` (f cos(theta_i); with `unit`, the
        // density of the direction that Sample_f returns). It returns true with the direction it leaves in and its weight.
        private bool Run(Vector3f wo, ref RNG rng, TransportMode mode, bool evaluate, bool unit, Vector3f wi, ref SampledSpectrum sum, out Vector3f exit,
                         out SampledSpectrum weight, out bool specularExit)
        {
            exit = default;
            weight = new SampledSpectrum(1f);
            specularExit = true;
            Field field = new Field();
            mg.Vec3d wid = Unit(V3(wi));
            bool above = wo.z > 0;
            mg.Vec3d o, d;
            int own = -1;
            // what the vertex before was: 1 a wide scattering (or the start), 2 a mirror on a grain drawn for it, 3 on a known one
            int before = 1;
            lastEnd = 0;
            bool onBase = false, covered = false;
            if (above)
            {
                o = new mg.Vec3d(0, 0, top);
                d = Unit(new mg.Vec3d(-wo.x, -wo.y, -wo.z));
            }
            else
            {
                // from below: a point of the base, under a grain or not
                o = new mg.Vec3d(0, 0, 0);
                d = Unit(new mg.Vec3d(-wo.x, -wo.y, -wo.z));
                onBase = true;
                covered = rng.Uniform<double>() < dist.tauBar0;
                if (!covered)
                {
                    Segment point;
                    point.o = o;
                    point.d = new mg.Vec3d(0, 0, 1);
                    point.length = 0;
                    field.free.Add(point);
                }
            }
            for (int order = 1; order <= maxOrder; ++order)
            {
                bool counted = evaluate && (unit || !(closedFirst && above && order == 1));
                if (!onBase)
                {
                    // The mirror grains that would send this ray to wi
                    if (counted && wi.z > 0 && Math.Abs(d.z) > 1e-9)
                    {
                        mg.Vec3d n = new mg.Vec3d(wid.x - d.x, wid.y - d.y, wid.z - d.z);
                        double norm = Math.Sqrt(n.x * n.x + n.y * n.y + n.z * n.z);
                        if (norm > 1e-9)
                        {
                            n = new mg.Vec3d(n.x / norm, n.y / norm, n.z / norm);
                        }
                        for (int j = 0; j < dist.n && norm > 1e-9 && n.z > 0; ++j)
                        {
                            if (!paper.GrainIsSpecular(j))
                            {
                                continue;
                            }
                            mg.Vec3d u;
                            double len = dist.MapNormal(j, n, out u);
                            double h = dist.r[j] * u.z;
                            double t = (h - o.z) / d.z;
                            if (!(t > 1e-9 * top))
                            {
                                continue;
                            }
                            mg.Vec3d hit = Along(o, d, t);
                            Grain g;
                            g.type = j;
                            g.cx = hit.x - dist.r[j] * (dist.M[j, 0] * u.x + dist.M[j, 1] * u.y);
                            g.cy = hit.y - dist.r[j] * (dist.M[j, 2] * u.x + dist.M[j, 3] * u.y);
                            bool consistent = true;
                            for (int s = 0; s < field.free.Count && consistent; ++s)
                            {
                                consistent = !Touches(g, field.free[s]);
                            }
                            if (!consistent)
                            {
                                continue;
                            }
                            int index = field.grains.Count;
                            field.grains.Add(g);
                            double tFirst;
                            int first;
                            bool reached = Trace(field, o, d, own, false, ref rng, out tFirst, out first) == OnGrain && first == index;
                            bool through = false;
                            if (reached)
                            {
                                Segment to;
                                to.o = o;
                                to.d = d;
                                to.length = t;
                                field.free.Add(to);
                                through = Leaves(field, hit, wid, index, ref rng);
                                field.free.RemoveAt(field.free.Count - 1);
                            }
                            field.grains.RemoveAt(index);
                            if (!through)
                            {
                                continue;
                            }
                            Float cosine = (Float)Math.Abs(d.x * n.x + d.y * n.y + d.z * n.z);
                            SampledSpectrum fresnel;
                            Float pSpecular = paper.GrainSpecularProbability(j, cosine, out fresnel);
                            double curvature = len * len * len * len / (dist.r[j] * dist.r[j] * dist.detM[j] * dist.detM[j]);
                            Float density = (Float)(rho[j] / (4 * Math.Abs(d.z) * curvature));
                            sum += unit ? weight * (density * pSpecular) : weight * fresnel * density;
                        }
                    }
                    if (evaluate && order < maxOrder && wi.z > 0 && Math.Abs(d.z) > 1e-9 && field.grains.Count > 0)
                    {
                        SampledSpectrum viaFresh = weight * FreshThenKnown(field, o, d, own, wid, unit, ref rng);
                        captured[1] += viaFresh[0];
                        sum += viaFresh;
                        if (chains)
                        {
                            SampledSpectrum chained = weight * ChainsFromFresh(field, o, d, own, wid, unit, ref rng);
                            captured[2] += chained[0];
                            sum += chained;
                        }
                    }
                    double tHit;
                    int grain;
                    int knownBefore = field.grains.Count;
                    int end = Trace(field, o, d, own, true, ref rng, out tHit, out grain);
                    if (end == Lost)
                    {
                        return false;
                    }
                    Segment travelled;
                    travelled.o = o;
                    travelled.d = d;
                    travelled.length = tHit;
                    field.free.Add(travelled);
                    if (end == Escaped)
                    {
                        exit = F3(d);
                        return true;
                    }
                    o = Along(o, d, tHit);
                    if (end == Base)
                    {
                        o = new mg.Vec3d(o.x, o.y, 0);
                        onBase = true;
                        covered = false;
                        own = -1;
                    }
                    else
                    {
                        // A grain: a mirror of its material, or diffuse
                        Grain g = field.grains[grain];
                        mg.Vec3d n = NormalAt(g, o);
                        double cosIn = -(d.x * n.x + d.y * n.y + d.z * n.z);
                        if (!(cosIn > 0))
                        {
                            return false;
                        }
                        int j = g.type;
                        SampledSpectrum fresnel;
                        Float pSpecular = paper.GrainSpecularProbability(j, (Float)cosIn, out fresnel);
                        SampledSpectrum Kd = paper.GrainDiffuse(j);
                        SampledSpectrum one = new SampledSpectrum(1f);
                        if (counted && (bool)Kd && wi.z > 0)
                        {
                            double cosOut = wid.x * n.x + wid.y * n.y + wid.z * n.z;
                            if (cosOut > 0 && Leaves(field, o, wid, grain, ref rng))
                            {
                                SampledSpectrum unused;
                                paper.GrainSpecularProbability(j, (Float)cosOut, out unused);
                                sum += unit ? weight * ((1 - pSpecular) * (Float)cosOut * InvPi)
                                            : weight * Kd * (one - fresnel) * (one - unused) * ((Float)cosOut * InvPi);
                            }
                        }
                        if (evaluate && order < maxOrder && (bool)Kd && wi.z > 0)
                        {
                            mg.Vec3d normal = n;
                            Func<mg.Vec3d, SampledSpectrum> fromGrain = (mg.Vec3d to) =>
                            {
                                double cosTo = to.x * normal.x + to.y * normal.y + to.z * normal.z;
                                if (!(cosTo > 0))
                                {
                                    return new SampledSpectrum(0f);
                                }
                                if (unit)
                                {
                                    return new SampledSpectrum((1 - pSpecular) * (Float)cosTo * InvPi);
                                }
                                SampledSpectrum leavingTo;
                                paper.GrainSpecularProbability(j, (Float)cosTo, out leavingTo);
                                return Kd * (one - fresnel) * (one - leavingTo) * ((Float)cosTo * InvPi);
                            };
                            SampledSpectrum viaGrain = weight * ViaKnownMirrors(field, o, grain, wid, unit, fromGrain, ref rng);
                            captured[0] += viaGrain[0];
                            sum += viaGrain;
                            if (chains)
                            {
                                SampledSpectrum chained = weight * ChainsFromWide(field, o, grain, wid, unit, fromGrain, ref rng);
                                captured[2] += chained[0];
                                sum += chained;
                            }
                        }
                        if (rng.Uniform<Float>() < pSpecular)
                        {
                            lastEnd = grain < knownBefore ? before : 0;
                            before = grain < knownBefore ? 3 : 2;
                            d = new mg.Vec3d(d.x + 2 * cosIn * n.x, d.y + 2 * cosIn * n.y, d.z + 2 * cosIn * n.z);
                            if (!unit)
                            {
                                weight *= fresnel / pSpecular;
                            }
                        }
                        else
                        {
                            if (!(bool)Kd)
                            {
                                return false;
                            }
                            lastEnd = 0;
                            before = 1;
                            Vector3f local = SampleCosineHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                            Frame frame = Frame.FromZ(F3(n));
                            Vector3f world = frame.FromLocal(local);
                            SampledSpectrum leaving;
                            paper.GrainSpecularProbability(j, AbsCosTheta(local), out leaving);
                            if (!unit)
                            {
                                weight *= Kd * (one - fresnel) * (one - leaving) / (1 - pSpecular);
                            }
                            d = Unit(V3(world));
                        }
                        own = grain;
                        specularExit = false;
                        if (!(bool)weight)
                        {
                            return false;
                        }
                        continue;
                    }
                }
                // The bulk, at a point of the base
                {
                    Vector3f wIn = new Vector3f(-(Float)d.x, -(Float)d.y, -(Float)d.z);
                    if (counted)
                    {
                        bool up = wi.z > 0;
                        bool open = !up || (!covered && Leaves(field, o, wid, -1, ref rng));
                        if (open)
                        {
                            if (unit)
                            {
                                sum += weight * bulk.PDF(wIn, wi, mode, BxDFReflTransFlags.All);
                            }
                            else
                            {
                                sum += weight * bulk.f(wIn, wi, mode) * AbsCosTheta(wi);
                            }
                        }
                    }
                    if (evaluate && order < maxOrder && wi.z > 0 && !covered)
                    {
                        Func<mg.Vec3d, SampledSpectrum> fromBase = (mg.Vec3d to) =>
                        {
                            Vector3f direction = F3(to);
                            if (unit)
                            {
                                return new SampledSpectrum(bulk.PDF(wIn, direction, mode, BxDFReflTransFlags.All));
                            }
                            return bulk.f(wIn, direction, mode) * AbsCosTheta(direction);
                        };
                        SampledSpectrum viaBase = weight * ViaKnownMirrors(field, o, -1, wid, unit, fromBase, ref rng);
                        captured[0] += viaBase[0];
                        sum += viaBase;
                        if (chains)
                        {
                            SampledSpectrum chained = weight * ChainsFromWide(field, o, -1, wid, unit, fromBase, ref rng);
                            captured[2] += chained[0];
                            sum += chained;
                        }
                    }
                    pstd.optional<BSDFSample> sampled = bulk.Sample_f(wIn, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), mode,
                                                                      BxDFReflTransFlags.All);
                    if (!sampled || sampled.value().pdf == 0 || !(bool)sampled.value().f)
                    {
                        return false;
                    }
                    BSDFSample bs = sampled.value();
                    lastEnd = 0;
                    before = 1;
                    if (!unit)
                    {
                        weight *= bs.f * (AbsCosTheta(bs.wi) / bs.pdf);
                    }
                    specularExit &= bs.IsSpecular();
                    if (bs.wi.z < 0)
                    {
                        exit = bs.wi;
                        return true;
                    }
                    if (covered)
                    {
                        return false;
                    }
                    d = Unit(V3(bs.wi));
                    onBase = false;
                    own = -1;
                }
            }
            return false;
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            if (wo.z == 0 || wi.z == 0)
            {
                return new SampledSpectrum(0f);
            }
            bool first = closedFirst && wo.z > 0;
            SampledSpectrum result = first ? paper.f(wo, wi, mode) : new SampledSpectrum(0f);
            if (maxOrder < (first ? 2 : 1))
            {
                return result;
            }
            RNG rng = new RNG(Hash(GetOptions().seed, wo, 0x45584143UL), Hash(wi));
            SampledSpectrum sum = new SampledSpectrum(0f);
            double wideBefore = captured[0], freshBefore = captured[1], chainsBefore = captured[2];
            for (int s = 0; s < nSamples; ++s)
            {
                Run(wo, ref rng, mode, true, false, wi, ref sum, out Vector3f _, out SampledSpectrum _, out bool _);
            }
            captured[0] = wideBefore + (captured[0] - wideBefore) / AbsCosTheta(wi);
            captured[1] = freshBefore + (captured[1] - freshBefore) / AbsCosTheta(wi);
            captured[2] = chainsBefore + (captured[2] - chainsBefore) / AbsCosTheta(wi);
            for (int c = 0; c < NSpectrumSamples; ++c)
            {
                if (!Float.IsFinite(sum[c]) || sum[c] < 0)
                {
                    sum[c] = 0;
                }
            }
            return result + sum / (nSamples * AbsCosTheta(wi));
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if (wo.z == 0 || wi.z == 0)
            {
                return 0;
            }
            RNG rng = new RNG(Hash(GetOptions().seed, wo, 0x50444658UL), Hash(wi));
            SampledSpectrum sum = new SampledSpectrum(0f);
            Run(wo, ref rng, mode, true, true, wi, ref sum, out Vector3f _, out SampledSpectrum _, out bool _);
            // (one path often does not reach wi; like pbrt's layered BxDF, the estimate is mixed with a uniform density so that
            // the weights of multiple importance sampling stay finite)
            Float density = sum[0];
            return Lerp(0.9f, 1 / (4 * Pi), Float.IsFinite(density) && density > 0 ? density : 0);
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if (wo.z == 0)
            {
                return default;
            }
            RNG rng = new RNG(Hash(wo, uc), Hash(u));
            SampledSpectrum none = new SampledSpectrum(0f), weight;
            Vector3f wi;
            bool specular;
            if (!Run(wo, ref rng, mode, false, false, default, ref none, out wi, out weight, out specular) || wi.z == 0 || !(bool)weight)
            {
                return default;
            }
            bool reflect = wi.z * wo.z > 0;
            if ((sampleFlags & (reflect ? BxDFReflTransFlags.Reflection : BxDFReflTransFlags.Transmission)) == 0)
            {
                return default;
            }
            SampledSpectrum value = weight / AbsCosTheta(wi);
            if (specular)
            {
                return new BSDFSample(value, wi, 1, reflect ? BxDFFlags.SpecularReflection : BxDFFlags.SpecularTransmission, 1, false);
            }
            return new BSDFSample(value, wi, 1, reflect ? BxDFFlags.GlossyReflection : BxDFFlags.GlossyTransmission, 1, true);
        }

        private PolyGrainBxDF paper;
        private mg.PolyGrain dist;
        private BxDF bulk;
        private int nSamples = 4;
        private double[] rho = new double[mg.PolyGrain.kMaxTypes], reach = new double[mg.PolyGrain.kMaxTypes];
        private double top = 0, widest = 0;
        public int maxOrder = 32;
        public bool closedFirst = true;
        // (for the tests) How the last path of Run() ended: 0 any other way; by a mirror reflection on a grain it already knew,
        // 1 after a wide scattering, 2 after a mirror on a grain drawn for it, 3 after a mirror on a known grain
        public int lastEnd = 0;
        public double[] captured = new double[3];
        // The paths that leave by two or more mirrors on known grains are part of f() and PDF() (false: the second attempt)
        public bool chains = true;
    }

    // ExactPolyGrainMaterial Definition: the parameters of "polygrain", traced
    public sealed unsafe class ExactPolyGrainMaterial : Material
    {
        public PolyGrainMaterial grains;
        public int scatterDepth = 32;
        public bool closedFirst = true;
        public bool chains = true;

        public static string Name() { return "ExactPolyGrainMaterial"; }

        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval) { return grains.CanEvaluateTextures(texEval); }
        public override FloatTexture GetDisplacement() { return grains.GetDisplacement(); }
        public override Image GetNormalMap() { return grains.GetNormalMap(); }
        public override bool HasSubsurfaceScattering() { return false; }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return null;
        }

        public override string ToString() { return StringPrintf("[ ExactPolyGrainMaterial scatterDepth: %d grains: %s ]", scatterDepth, grains); }

        public ExactPolyGrainBxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda)
            where TextureEvaluator : ITextureEvaluator
        {
            PolyGrainBxDF paper = grains.GetBxDF(texEval, ctx, ref lambda);
            paper.maxOrder = 1;
            ExactPolyGrainBxDF traced = new ExactPolyGrainBxDF(paper, scatterDepth, closedFirst);
            traced.chains = chains;
            return traced;
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(GetBxDF(texEval, ctx, ref lambda)));
        }

        public static ExactPolyGrainMaterial Create(TextureParameterDictionary parameters, Image normalMap, FileLoc loc, Allocator alloc)
        {
            ExactPolyGrainMaterial material = new ExactPolyGrainMaterial();
            material.scatterDepth = parameters.GetOneInt("scatterdepth", 32);
            material.closedFirst = parameters.GetOneBool("closedfirst", true);
            material.chains = parameters.GetOneBool("chains", true);
            material.grains = PolyGrainMaterial.Create(parameters, normalMap, loc, alloc);
            return material;
        }
    }
}
