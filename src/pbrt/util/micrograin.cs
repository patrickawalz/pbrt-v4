// (not in pbrt-v4) micrograin.cs
//
// A direct port of util/micrograin.h of the C++ fork (the namespace pbrt::mg is the static class mg).
//
// Exact geometry for the micrograin framework of
//   S. Lucas, M. Ribardiere, R. Pacanowski, P. Barla,
//   "A Fully-correlated Anisotropic Micrograin BSDF Model", ACM TOG 43(4), 2024 (LRPB24)
// as used by
//   K. Xu, S. Lucas, M. Ribardiere, B. Bringier, P. Barla,
//   "A Discrete Polydisperse Anisotropic BSDF Model based on the Micrograin Framework",
//   Computer Graphics Forum, 2026 (XLRBB26).
//
// The quantity needed by the distant masking-shadowing term (LRPB24 Eq. 17-19) is the planar area of the union of the two
// shadows that a unit spherical micrograin casts, onto a plane at height h, from two directions i and o:
//     sigma_s(i,h) + sigma_s(o,h) - sigma_cap(i,o,h).
// LRPB24 gives closed forms (Appendices A and B). Here they are re-derived in a form that is robust for every configuration
// and evaluated exactly (no quadrature of the area, no approximation):
//
//  * In the plane H at height h (unit sphere centred at the origin), the shadow of direction d (polar angle theta, azimuth
//    unit vector u) is
//        S_d = (E_d  intersect  Hp_d)  minus  C
//    with C   the disc |x| <= sqrt(1-h^2)  (the sphere's own cross-section),
//         E_d the ellipse  cos^2(theta) ((x.u) - h tan(theta))^2 + (x.u_perp)^2 <= 1
//             (points whose line along d passes within distance 1 of the centre),
//         Hp_d the half plane  x.u <= -h cot(theta)
//             (the sphere lies ahead of the point along d, not behind it).
//    The chord bounding Hp_d passes through the points where C is tangent to E_d, so S_d is an elliptic segment minus a
//    circular segment. This gives sigma_s in closed form (identical to LRPB24 Appendix A).
//
//  * C is contained in E_d, so   S_i cap S_o = (E_i cap E_o cap Hp_i cap Hp_o) \ (C cap Hp_i cap Hp_o). Both terms are areas
//    of convex regions bounded by conics and lines. They are evaluated exactly by splitting at every vertex and integrating
//    the (analytic) upper and lower boundary functions between vertices. The two cylinders along i and o have equal radius,
//    so their boundary curves meet on the two planes p.(i-o) = 0 and p.(i+o) = 0 (LRPB24 Fig. 19, plane P), which reduces the
//    ellipse/ellipse vertices to line/ellipse intersections (quadratics).

using System;

namespace pbrt
{
    public static unsafe class mg
    {
        public struct Vec3d
        {
            public double x, y, z;

            public Vec3d(double x, double y, double z)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }
        }

        // (p-c)^T Q (p-c) <= 1, Q = [[qxx,qxy],[qxy,qyy]] (positive definite).
        public struct Ell
        {
            public double cx, cy, qxx, qxy, qyy;

            public Ell(double cx, double cy, double qxx, double qxy, double qyy)
            {
                this.cx = cx;
                this.cy = cy;
                this.qxx = qxx;
                this.qxy = qxy;
                this.qyy = qyy;
            }
        }

        // nx*x + ny*y <= d
        public struct HalfPlane
        {
            public double nx, ny, d;
        }

        private struct Fn
        {
            public int kind;     // 0: line  y = m x + b;  1: ellipse upper;  2: ellipse lower
            public double m, b;  // for kind 0
            public int e;        // ellipse index for kinds 1,2

            public Fn(int kind, double m, double b, int e)
            {
                this.kind = kind;
                this.m = m;
                this.b = b;
                this.e = e;
            }
        }

        private static double EllDisc(Ell E, double x)
        {
            double dx = x - E.cx;
            double detQ = E.qxx * E.qyy - E.qxy * E.qxy;
            return E.qyy - detQ * dx * dx;
        }

        private static double EvalFn(Fn f, Ell* E, double x)
        {
            if (f.kind == 0)
            {
                return f.m * x + f.b;
            }
            Ell el = E[f.e];
            double dx = x - el.cx;
            double s = Math.Sqrt(Math.Max(0.0, EllDisc(el, x))) / el.qyy;
            double b = el.cy - (el.qxy / el.qyy) * dx;
            return f.kind == 1 ? b + s : b - s;
        }

        // integral of sqrt(a - b u^2) du = u/2 sqrt(a-bu^2) + a/(2 sqrt b) asin(u sqrt(b/a))
        private static double RootIntegral(double a, double bq, double u)
        {
            double disc = Math.Max(0.0, a - bq * u * u);
            double arg = Math.Max(-1.0, Math.Min(1.0, u * Math.Sqrt(bq / a)));
            return 0.5 * u * Math.Sqrt(disc) + a / (2 * Math.Sqrt(bq)) * Math.Asin(arg);
        }

        private static double IntegrateFn(Fn f, Ell* E, double xa, double xb)
        {
            if (f.kind == 0)
            {
                return 0.5 * f.m * (xb * xb - xa * xa) + f.b * (xb - xa);
            }
            Ell el = E[f.e];
            double detQ = el.qxx * el.qyy - el.qxy * el.qxy;
            double lin = el.cy * (xb - xa) - (el.qxy / el.qyy) * (0.5 * ((xb - el.cx) * (xb - el.cx) - (xa - el.cx) * (xa - el.cx)));
            double sq = (RootIntegral(el.qyy, detQ, xb - el.cx) - RootIntegral(el.qyy, detQ, xa - el.cx)) / el.qyy;
            return f.kind == 1 ? lin + sq : lin - sq;
        }

        // Roots of A t^2 + 2 B t + C = 0; returns count and writes roots.
        private static int SolveQuadratic(double A, double B, double C, double* r)
        {
            if (Math.Abs(A) < 1e-300)
            {
                if (Math.Abs(B) < 1e-300)
                {
                    return 0;
                }
                r[0] = -C / (2 * B);
                return 1;
            }
            double disc = B * B - A * C;
            if (disc < 0)
            {
                return 0;
            }
            double s = Math.Sqrt(disc);
            // numerically stable
            double q = -(B + (B >= 0 ? s : -s));
            double t0 = q / A;
            double t1 = (q != 0) ? C / q : t0;
            r[0] = t0;
            r[1] = t1;
            return 2;
        }

        // Area of the convex set  (intersection of all ellipses) and (all half planes). extraX are additional x positions at
        // which the active boundary may change (ellipse/ellipse vertices, supplied by the caller).
        public static double ConvexArea(Ell* E, int nE, HalfPlane* H, int nH, double* extraX, int nExtra)
        {
            const int kMaxX = 64;
            double* xs = stackalloc double[kMaxX];
            int nx = 0;
            double xlo = -1e300, xhi = 1e300;
            for (int e = 0; e < nE; ++e)
            {
                double detQ = E[e].qxx * E[e].qyy - E[e].qxy * E[e].qxy;
                double w = Math.Sqrt(E[e].qyy / detQ);
                if (nx < kMaxX && Double.IsFinite(E[e].cx - w))
                {
                    xs[nx++] = E[e].cx - w;
                }
                if (nx < kMaxX && Double.IsFinite(E[e].cx + w))
                {
                    xs[nx++] = E[e].cx + w;
                }
                xlo = Math.Max(xlo, E[e].cx - w);
                xhi = Math.Min(xhi, E[e].cx + w);
            }
            const double kVert = 1e-13;
            for (int k = 0; k < nH; ++k)
            {
                if (Math.Abs(H[k].ny) < kVert)
                {
                    // vertical boundary: x bound
                    double xb = H[k].d / H[k].nx;
                    if (H[k].nx > 0)
                    {
                        xhi = Math.Min(xhi, xb);
                    }
                    else
                    {
                        xlo = Math.Max(xlo, xb);
                    }
                    if (nx < kMaxX && Double.IsFinite(xb))
                    {
                        xs[nx++] = xb;
                    }
                }
            }
            // line/ellipse and line/line vertices
            double* r = stackalloc double[2];
            for (int k = 0; k < nH; ++k)
            {
                if (Math.Abs(H[k].ny) < kVert)
                {
                    continue;
                }
                double beta = -H[k].nx / H[k].ny;
                for (int e = 0; e < nE; ++e)
                {
                    Ell el = E[e];
                    double alpha = (H[k].d - H[k].nx * el.cx) / H[k].ny - el.cy;
                    double A = el.qxx + 2 * el.qxy * beta + el.qyy * beta * beta;
                    double B = el.qxy * alpha + el.qyy * alpha * beta;
                    double C = el.qyy * alpha * alpha - 1;
                    int n = SolveQuadratic(A, B, C, r);
                    for (int q = 0; q < n; ++q)
                    {
                        if (nx < kMaxX && Double.IsFinite(el.cx + r[q]))
                        {
                            xs[nx++] = el.cx + r[q];
                        }
                    }
                }
                for (int l = k + 1; l < nH; ++l)
                {
                    double det = H[k].nx * H[l].ny - H[k].ny * H[l].nx;
                    if (Math.Abs(det) < 1e-300)
                    {
                        continue;
                    }
                    double x = (H[k].d * H[l].ny - H[k].ny * H[l].d) / det;
                    if (nx < kMaxX && Double.IsFinite(x))
                    {
                        xs[nx++] = x;
                    }
                }
            }
            for (int k = 0; k < nExtra; ++k)
            {
                if (nx < kMaxX && Double.IsFinite(extraX[k]))
                {
                    xs[nx++] = extraX[k];
                }
            }
            if (xhi <= xlo)
            {
                return 0;
            }
            // keep points inside [xlo,xhi], add the bounds
            double* cl = stackalloc double[kMaxX + 2];
            int nc = 0;
            cl[nc++] = xlo;
            cl[nc++] = xhi;
            for (int k = 0; k < nx; ++k)
            {
                if (xs[k] > xlo && xs[k] < xhi)
                {
                    cl[nc++] = xs[k];
                }
            }
            new Span<double>(cl, nc).Sort();
            double area = 0;
            for (int k = 0; k + 1 < nc; ++k)
            {
                double xa = cl[k], xb = cl[k + 1];
                if (!(xb - xa > 1e-15))
                {
                    continue;
                }
                double xm = 0.5 * (xa + xb);
                // find active top (min) and bottom (max)
                Fn top = default, bot = default;
                double topv = 1e300, botv = -1e300;
                bool ok = true;
                for (int e = 0; e < nE && ok; ++e)
                {
                    if (EllDisc(E[e], xm) < 0)
                    {
                        ok = false;
                        break;
                    }
                    Fn ft = new Fn(1, 0, 0, e), fb = new Fn(2, 0, 0, e);
                    double vt = EvalFn(ft, E, xm), vb = EvalFn(fb, E, xm);
                    if (vt < topv)
                    {
                        topv = vt;
                        top = ft;
                    }
                    if (vb > botv)
                    {
                        botv = vb;
                        bot = fb;
                    }
                }
                if (!ok)
                {
                    continue;
                }
                for (int q = 0; q < nH; ++q)
                {
                    if (Math.Abs(H[q].ny) < kVert)
                    {
                        continue;
                    }
                    Fn f = new Fn(0, -H[q].nx / H[q].ny, H[q].d / H[q].ny, 0);
                    double v = f.m * xm + f.b;
                    if (H[q].ny > 0)
                    {
                        if (v < topv)
                        {
                            topv = v;
                            top = f;
                        }
                    }
                    else
                    {
                        if (v > botv)
                        {
                            botv = v;
                            bot = f;
                        }
                    }
                }
                if (!(topv > botv))
                {
                    continue;
                }
                area += IntegrateFn(top, E, xa, xb) - IntegrateFn(bot, E, xa, xb);
            }
            return area;
        }

        // ---------------------------------------------------------------------------
        // Unit spherical micrograin: shadow areas on the plane at height h in [0,1). Directions are unit vectors with z > 0.

        // sigma_s(d, h): area of the shadow cast from direction d (LRPB24 Appendix A).
        public static double ShadowArea(Vec3d d, double h)
        {
            double sinT = Math.Sqrt(d.x * d.x + d.y * d.y);
            if (!(h < 1.0) || !(sinT > h))
            {
                return 0;
            }
            double cosT = d.z;
            double w = h / sinT;
            double segE = (Math.Acos(w) - w * Math.Sqrt(Math.Max(0.0, 1 - w * w))) / cosT;
            double rho2 = 1 - h * h;
            double rho = Math.Sqrt(rho2);
            double wc = Math.Min(1.0, h * cosT / (sinT * rho));
            double segC = rho2 * (Math.Acos(wc) - wc * Math.Sqrt(Math.Max(0.0, 1 - wc * wc)));
            return Math.Max(0.0, segE - segC);
        }

        // Shadow constraint set of direction d at height h: ellipse and half plane in plane coords.
        private static void ShadowConstraints(Vec3d d, double h, Ell* E, HalfPlane* H)
        {
            double sinT = Math.Sqrt(d.x * d.x + d.y * d.y), cosT = d.z;
            double ux = d.x / sinT, uy = d.y / sinT;
            double tanT = sinT / cosT;
            // Q = cos^2 u u^T + u_perp u_perp^T,  u_perp = (-uy, ux)
            double c2 = cosT * cosT;
            E->cx = h * tanT * ux;
            E->cy = h * tanT * uy;
            E->qxx = c2 * ux * ux + uy * uy;
            E->qxy = c2 * ux * uy - ux * uy;
            E->qyy = c2 * uy * uy + ux * ux;
            H->nx = ux;
            H->ny = uy;
            H->d = -h * cosT / sinT;
        }

        // sigma_cap(i,o,h): area of the overlap of the shadows cast from i and o (LRPB24 Appendix B), exact.
        public static double ShadowOverlapArea(Vec3d i, Vec3d o, double h)
        {
            double si = ShadowArea(i, h), so = ShadowArea(o, h);
            if (si <= 0 || so <= 0)
            {
                return 0;
            }
            double dx = i.x - o.x, dy = i.y - o.y, dz = i.z - o.z;
            if (dx * dx + dy * dy + dz * dz < 1e-24)
            {
                return si;  // identical directions: identical shadows
            }
            Ell* E = stackalloc Ell[2];
            HalfPlane* H = stackalloc HalfPlane[2];
            ShadowConstraints(i, h, &E[0], &H[0]);
            ShadowConstraints(o, h, &E[1], &H[1]);
            // Vertices of E_i and E_o: on the lines  p_xy.(i-o)_xy = -h (i-o)_z  and  p_xy.(i+o)_xy = -h (i+o)_z
            // (intersections of the planes p.(i-+o)=0 with H).
            double* extra = stackalloc double[8];
            double* r = stackalloc double[2];
            int nExtra = 0;
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                double nxv = i.x + sgn * o.x, nyv = i.y + sgn * o.y, nzv = i.z + sgn * o.z;
                double len = Math.Sqrt(nxv * nxv + nyv * nyv);
                if (len < 1e-12)
                {
                    continue;
                }
                nxv /= len;
                nyv /= len;
                double dd = -h * nzv / len;  // line n.p = dd
                if (Math.Abs(nyv) < 1e-13)
                {
                    // vertical line x = dd/nxv: it is the breakpoint itself
                    if (nExtra < 8)
                    {
                        extra[nExtra++] = dd / nxv;
                    }
                    continue;
                }
                double beta = -nxv / nyv;
                Ell el = E[0];
                double alpha = (dd - nxv * el.cx) / nyv - el.cy;
                double A = el.qxx + 2 * el.qxy * beta + el.qyy * beta * beta;
                double B = el.qxy * alpha + el.qyy * alpha * beta;
                double C = el.qyy * alpha * alpha - 1;
                int n = SolveQuadratic(A, B, C, r);
                for (int q = 0; q < n && nExtra < 8; ++q)
                {
                    extra[nExtra++] = el.cx + r[q];
                }
            }
            double a1 = ConvexArea(E, 2, H, 2, extra, nExtra);
            double rho2 = 1 - h * h;
            Ell C0 = new Ell(0, 0, 1.0 / rho2, 0, 1.0 / rho2);
            double a2 = (rho2 > 0) ? ConvexArea(&C0, 1, H, 2, null, 0) : 0.0;
            double a = a1 - a2;
            return Math.Max(0.0, Math.Min(a, Math.Min(si, so)));
        }

        // Total shadowed area  sigma_s(i,h)+sigma_s(o,h)-sigma_cap(i,o,h)  (union of shadows).
        public static double ShadowUnionArea(Vec3d i, Vec3d o, double h)
        {
            return ShadowArea(i, h) + ShadowArea(o, h) - ShadowOverlapArea(i, o, h);
        }

        // ---------------------------------------------------------------------------
        // Discrete polydisperse micrograin distribution (XLRBB26 Sections 5-6, Appendices D, E).
        //
        // Grain type j has radius r_j, surface filling factor tau_j (= tau_{j,0}) and a lateral stretching matrix M_j (2x2, maps
        // the unit spherical micrograin to the ellipsoid; the z axis is unchanged). Only ratios of radii matter. Writing
        // a_j = -ln(1-tau_j)/r_j^2 (= rho*pi*omega_j*|det M_j|) every statistic of the model is expressed through
        //     Lambda(h) = sum_j a_j [r_j^2 - h^2]^+            ( tau_bar(h) = 1 - exp(-Lambda(h)) ).
        // The type-restricted joint density of normal and type (XLRBB26 Eq. 14) is
        //     p(i,m) = cos(theta_m) |det M_i| a_i r_i^2 exp(-Lambda(h_i(m))) / (tau_bar0 pi |M_i^T m|^4)
        // with h_i(m) = r_i m_z/|M_i^T m|; D(m) = sum_i p(i,m)/cos(theta_m) (Eq. 11).
        public sealed class PolyGrain
        {
            public const int kMaxTypes = 8;
            public int n = 0;
            public double[] tau = new double[kMaxTypes], r = new double[kMaxTypes];
            public double[,] M = new double[kMaxTypes, 4];     // row-major 2x2 lateral matrix
            public double[,] Minv = new double[kMaxTypes, 4];  // its inverse
            public double[] detM = new double[kMaxTypes];
            // derived
            public double[] a = new double[kMaxTypes];  // -ln(1-tau)/r^2
            public double tauBar0;
            public int[] order = new int[kMaxTypes];    // type indices sorted by increasing radius
            // slices k = 0..n-1 (in sorted order): h in [rLo[k], rHi[k])
            public double[] rLo = new double[kMaxTypes], rHi = new double[kMaxTypes];
            public double[] sliceA = new double[kMaxTypes], sliceB = new double[kMaxTypes];  // Lambda(h) = A - B h^2 inside slice
            public double[] pSlice = new double[kMaxTypes];                                    // p(S_k)
            public double[] pSliceCdf = new double[kMaxTypes];
            public double[] typeProb = new double[kMaxTypes];                                  // P_i = integral p(i,m) dm

            // beta/rotation parametrisation: M = R(psi) diag(1/bx, 1/by) R(psi)^T (as LRPB24: lateral semi-axes r/bx, r/by).
            public void SetType(int j, double tau_, double r_, double bx, double by, double psi)
            {
                tau[j] = tau_;
                r[j] = r_;
                double c = Math.Cos(psi), s = Math.Sin(psi);
                double sx = 1 / bx, sy = 1 / by;
                // M = R diag(sx,sy) R^T
                M[j, 0] = c * c * sx + s * s * sy;
                M[j, 1] = c * s * (sx - sy);
                M[j, 2] = M[j, 1];
                M[j, 3] = s * s * sx + c * c * sy;
                double sxi = bx, syi = by;
                Minv[j, 0] = c * c * sxi + s * s * syi;
                Minv[j, 1] = c * s * (sxi - syi);
                Minv[j, 2] = Minv[j, 1];
                Minv[j, 3] = s * s * sxi + c * c * syi;
                detM[j] = sx * sy;
            }

            public void Finalize_()
            {
                double prod = 1;
                for (int j = 0; j < n; ++j)
                {
                    a[j] = -ff.Log1p(-tau[j]) / (r[j] * r[j]);
                    prod *= (1 - tau[j]);
                    order[j] = j;
                }
                tauBar0 = 1 - prod;
                // (std::sort of the indices by the radius: an insertion sort, n is 8 at most)
                for (int p = 1; p < n; ++p)
                {
                    int v = order[p];
                    int q = p - 1;
                    while (q >= 0 && r[order[q]] > r[v])
                    {
                        order[q + 1] = order[q];
                        --q;
                    }
                    order[q + 1] = v;
                }
                double cdf = 0, prev = 0;
                for (int k = 0; k < n; ++k)
                {
                    double A = 0, B = 0;
                    for (int q = k; q < n; ++q)
                    {
                        int j = order[q];
                        A += a[j] * r[j] * r[j];
                        B += a[j];
                    }
                    sliceA[k] = A;
                    sliceB[k] = B;
                    rLo[k] = prev;
                    rHi[k] = r[order[k]];
                    prev = rHi[k];
                    // p(S_k) = (e^{-A+B rHi^2} - e^{-A+B rLo^2}) / tauBar0
                    double e1 = Math.Exp(-A + B * rHi[k] * rHi[k]);
                    double e0 = Math.Exp(-A + B * rLo[k] * rLo[k]);
                    pSlice[k] = (e1 - e0) / tauBar0;
                    cdf += pSlice[k];
                    pSliceCdf[k] = cdf;
                }
                for (int i = 0; i < n; ++i)
                {
                    typeProb[i] = 0;
                }
                for (int k = 0; k < n; ++k)
                {
                    for (int q = k; q < n; ++q)
                    {
                        int j = order[q];
                        typeProb[j] += pSlice[k] * a[j] / sliceB[k];
                    }
                }
            }

            public double Lambda(double h)
            {
                double L = 0;
                for (int j = 0; j < n; ++j)
                {
                    if (r[j] > h)
                    {
                        L += a[j] * (r[j] * r[j] - h * h);
                    }
                }
                return L;
            }

            // sphere-space normal  m' = M^T m / |M^T m| ; returns |M^T m|. (M symmetric here but kept general: M^T m uses
            // lateral transpose.)
            public double MapNormal(int j, Vec3d m, out Vec3d mp)
            {
                double x = M[j, 0] * m.x + M[j, 2] * m.y;
                double y = M[j, 1] * m.x + M[j, 3] * m.y;
                double len = Math.Sqrt(x * x + y * y + m.z * m.z);
                mp = new Vec3d(x / len, y / len, m.z / len);
                return len;
            }

            // height reached by type j at normal m
            public double Height(int j, Vec3d m)
            {
                return r[j] * m.z / MapNormal(j, m, out Vec3d _);
            }

            // p(i,m)  (solid-angle density; XLRBB26 Eq. 14)
            public double PTypeNormal(int i, Vec3d m)
            {
                if (!(m.z > 0))
                {
                    return 0;
                }
                double len = MapNormal(i, m, out Vec3d _);
                double h = r[i] * m.z / len;
                double len2 = len * len;
                return m.z * Math.Abs(detM[i]) * a[i] * r[i] * r[i] * Math.Exp(-Lambda(h)) / (tauBar0 * Math.PI * len2 * len2);
            }

            // polydisperse NDF D(m) (Eq. 11): D(m) = sum_i p(i,m)/cos(theta_m)
            public double NDF(Vec3d m)
            {
                if (!(m.z > 0))
                {
                    return 0;
                }
                double s = 0;
                for (int i = 0; i < n; ++i)
                {
                    s += PTypeNormal(i, m);
                }
                return s / m.z;
            }

            // Sampling (Appendix E). Slice k (index in sorted order) and type i. Choose (k,i) with probability
            // p(S_k) p(i|S_k) = p(S_k) a_i / B_k  (i sorted index >= k).
            public void SampleSliceType(double u0, double u1, out int kOut, out int typeOut, out double pJoint)
            {
                int k = 0;
                while (k < n - 1 && u0 >= pSliceCdf[k])
                {
                    ++k;
                }
                // type within slice among order[k..n-1] with weights a_j
                double target = u1 * sliceB[k], acc = 0;
                int q = k;
                for (; q < n - 1; ++q)
                {
                    acc += a[order[q]];
                    if (target < acc)
                    {
                        break;
                    }
                }
                // ensure the chosen sorted index lies in [k, n-1]
                int j = order[q];
                kOut = k;
                typeOut = j;
                pJoint = pSlice[k] * a[j] / sliceB[k];
            }

            // Draw the normal inside (slice k, type i): height by inverting the slice CDF, sphere-space polar angle
            // cos = h / r_i, azimuth uniform, then map to the stretched space. Returns m and the density p(m | i, S_k) (Eq. 41).
            public bool SampleNormal(int k, int i, double uh, double uphi, out Vec3d m, out double pdf)
            {
                double B = sliceB[k];
                double lo2 = rLo[k] * rLo[k], hi2 = rHi[k] * rHi[k];
                // u = h^2 = lo2 + log1p(uh * expm1(B (hi2-lo2))) / B
                double u = lo2 + ff.Log1p(uh * ff.Expm1(B * (hi2 - lo2))) / B;
                u = Math.Max(lo2, Math.Min(hi2, u));
                double h = Math.Sqrt(u);
                double cosp = Math.Min(1.0, h / r[i]);
                double sinp = Math.Sqrt(Math.Max(0.0, 1 - cosp * cosp));
                double phi = 2 * Math.PI * uphi;
                Vec3d mp = new Vec3d(sinp * Math.Cos(phi), sinp * Math.Sin(phi), cosp);
                // m proportional to M^{-T} m'
                double x = Minv[i, 0] * mp.x + Minv[i, 2] * mp.y;
                double y = Minv[i, 1] * mp.x + Minv[i, 3] * mp.y;
                double len = Math.Sqrt(x * x + y * y + mp.z * mp.z);
                m = new Vec3d(x / len, y / len, mp.z / len);
                pdf = PNormalGivenSliceType(k, i, m);
                return pdf > 0;
            }

            // p(m | i, S_k)  (Eq. 41)
            public double PNormalGivenSliceType(int k, int i, Vec3d m)
            {
                if (!(m.z > 0))
                {
                    return 0;
                }
                double len = MapNormal(i, m, out Vec3d _);
                double h = r[i] * m.z / len;
                if (h < rLo[k] || h >= rHi[k])
                {
                    return 0;
                }
                double B = sliceB[k];
                double hi2 = rHi[k] * rHi[k], lo2 = rLo[k] * rLo[k];
                double cosp = m.z / len;
                // |det M| r_i^2 cos' B e^{B h^2} / (pi |M^T m|^3 (e^{B hi2} - e^{B lo2})) computed in a numerically stable form:
                //   e^{B h^2}/(e^{B hi2}-e^{B lo2}) = e^{B(h^2-hi2)} / (1 - e^{-B(hi2-lo2)})
                double den = -ff.Expm1(-B * (hi2 - lo2));
                double num = Math.Exp(B * (h * h - hi2));
                return Math.Abs(detM[i]) * r[i] * r[i] * cosp * B * num / (Math.PI * len * len * len * den);
            }

            // -------------------------------------------------------------------------
            // Distant masking-shadowing (XLRBB26 Eq. 12, LRPB24 Eq. 17-19)
            // direction in the macro frame -> unit sphere space of type j (E = M_j^{-1} on xy)
            public Vec3d ToSphereSpace(int j, Vec3d d)
            {
                double x = Minv[j, 0] * d.x + Minv[j, 1] * d.y;
                double y = Minv[j, 2] * d.x + Minv[j, 3] * d.y;
                double len = Math.Sqrt(x * x + y * y + d.z * d.z);
                return new Vec3d(x / len, y / len, d.z / len);
            }

            // monodisperse unidirectional distant GAF of type j at normalised height hn=h/r_j
            public double GdistUni(int j, Vec3d d, double hn)
            {
                if (hn >= 1)
                {
                    return 1;
                }
                double sigma = ShadowArea(ToSphereSpace(j, d), hn);
                return Math.Exp(ff.Log1p(-tau[j]) / Math.PI * sigma);
            }

            // monodisperse bidirectional distant GAF (shadow union) of type j
            public double GdistBi(int j, Vec3d di, Vec3d dout, double hn)
            {
                if (hn >= 1)
                {
                    return 1;
                }
                double sigma = ShadowUnionArea(ToSphereSpace(j, di), ToSphereSpace(j, dout), hn);
                return Math.Exp(ff.Log1p(-tau[j]) / Math.PI * sigma);
            }

            // effective distant GAF of a point on a grain of type i at height h (Eq. 12)
            public double GTilde(int i, Vec3d wo, Vec3d wi, Vec3d m)
            {
                double h = Height(i, m);
                double g = 1;
                for (int j = 0; j < n; ++j)
                {
                    double hn = h / r[j];
                    if (hn >= 1)
                    {
                        continue;
                    }
                    g *= GdistBi(j, wo, wi, hn);
                }
                return g;
            }

            // pore (bulk) visibility, unidirectional: prod_j Gdist_j(d, 0)
            public double BulkVisUni(Vec3d d)
            {
                double g = 1;
                for (int j = 0; j < n; ++j)
                {
                    g *= GdistUni(j, d, 0);
                }
                return g;
            }

            // bidirectional: prod_j Gdist_j(i,o,0)
            public double BulkVisBi(Vec3d di, Vec3d dout)
            {
                double g = 1;
                for (int j = 0; j < n; ++j)
                {
                    g *= GdistBi(j, di, dout, 0);
                }
                return g;
            }
        }
    }
}
