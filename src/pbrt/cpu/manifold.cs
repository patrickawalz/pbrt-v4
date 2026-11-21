// (not in pbrt-v4) cpu/manifold.cs
//
// The specular manifold (Jakob and Marschner, "Manifold Exploration", SIGGRAPH 2012): a chain of specular vertices x_1 .. x_n
// between two endpoints x_0 and x_n+1 satisfies, at each of them, a constraint on its generalised half vector,
//     h = normalize(wi + eta wo),   c = (h . s, h . t) = 0,
// with wi and wo the directions to the neighbours, eta the index on the side of wo over that on the side of wi (1 for a
// reflection), and s, t the tangents of the surface made orthogonal to its shading normal. The chains that satisfy it are a
// manifold with the two endpoints as coordinates: moving an endpoint moves the specular vertices with it. This file has
//   - the constraint and its Jacobian (the 2x2 blocks A_i, B_i, C_i: derivatives with respect to the (u, v) of x_i-1, x_i, x_i+1);
//   - the solve of the block tridiagonal system;
//   - the walk that moves x_n+1 to a target by Newton steps on x_1, each followed by tracing the chain again from x_0;
//   - the generalised geometric term G(x_0 <-> x_n+1) = G(x_0, x_1) |det(d x_1 / d x_n+1)|.
// The conventions of the Jacobian follow Lightmetrica v2's `manifoldutils.cpp` (MIT licence, Hisanari Otsu 2015), itself after
// the paper. The core is shared by the manifold perturbation, half vector space light transport and specular manifold
// next-event estimation.

using System;

namespace pbrt
{
    using static pbrt;

    // ManifoldMatrix Definition: a 2x2 matrix, m[row][column]
    public struct ManifoldMatrix
    {
        public Float m00, m01, m10, m11;

        public ManifoldMatrix(Float m00, Float m01, Float m10, Float m11)
        {
            this.m00 = m00;
            this.m01 = m01;
            this.m10 = m10;
            this.m11 = m11;
        }

        public static ManifoldMatrix operator *(ManifoldMatrix a, ManifoldMatrix b)
        {
            return new ManifoldMatrix(a.m00 * b.m00 + a.m01 * b.m10, a.m00 * b.m01 + a.m01 * b.m11,
                                      a.m10 * b.m00 + a.m11 * b.m10, a.m10 * b.m01 + a.m11 * b.m11);
        }

        public static ManifoldMatrix operator -(ManifoldMatrix a, ManifoldMatrix b)
        {
            return new ManifoldMatrix(a.m00 - b.m00, a.m01 - b.m01, a.m10 - b.m10, a.m11 - b.m11);
        }

        public static Point2f operator *(ManifoldMatrix a, Point2f v)
        {
            return new Point2f(a.m00 * v.x + a.m01 * v.y, a.m10 * v.x + a.m11 * v.y);
        }

        public Float Determinant()
        {
            return m00 * m11 - m01 * m10;
        }

        public bool Inverse(out ManifoldMatrix inverse)
        {
            Float determinant = Determinant();
            if (determinant == 0 || Float.IsNaN(determinant))
            {
                inverse = default;
                return false;
            }
            Float scale = 1 / determinant;
            inverse = new ManifoldMatrix(m11 * scale, -m01 * scale, -m10 * scale, m00 * scale);
            return true;
        }
    }

    // ManifoldVertex Definition: the geometry of a vertex of a chain. An endpoint needs its position and, to be moved, its
    // tangents; a specular vertex also its shading normal, the derivatives of that normal, and its relative index.
    public sealed unsafe class ManifoldVertex
    {
        public Point3f p;
        public Vector3f dpdu, dpdv;
        public Normal3f n;
        public Normal3f dndu, dndv;
        // The index on the side of the next vertex over that on the side of the previous one (1 for a reflection)
        public Float eta = 1;
        // Whether the chain is reflected at the vertex (otherwise it goes through the surface)
        public bool reflection = true;
        // Whether the half vector of the vertex is held to its normal (a specular vertex) or to a given direction
        public bool specular = true;

        public static ManifoldVertex Of(SurfaceInteraction intr)
        {
            ManifoldVertex vertex = new ManifoldVertex();
            vertex.p = intr.p();
            // (the tangents of the surface itself: the vertex moves on it; the normal that the specular lobe is about, and its
            // derivatives for the same (u, v), are those of the shading)
            vertex.dpdu = intr.dpdu;
            vertex.dpdv = intr.dpdv;
            vertex.n = intr.shading.n;
            vertex.dndu = intr.shading.dndu;
            vertex.dndv = intr.shading.dndv;
            return vertex;
        }

        // An endpoint that is not on a surface (or whose surface does not matter): any two tangents across the direction w
        public static ManifoldVertex Free(Point3f p, Vector3f w)
        {
            ManifoldVertex vertex = new ManifoldVertex();
            vertex.p = p;
            Vector3f tangentU, tangentV;
            CoordinateSystem(Normalize(w), &tangentU, &tangentV);
            vertex.dpdu = tangentU;
            vertex.dpdv = tangentV;
            vertex.n = (Normal3f)Normalize(w);
            return vertex;
        }
    }

    // SpecularManifold Definition
    public static unsafe class SpecularManifold
    {
        // The generalised half vector at a vertex, and the lengths that its derivatives need
        static Vector3f HalfVector(ManifoldVertex previous, ManifoldVertex vertex, ManifoldVertex next, out Vector3f wi, out Vector3f wo,
                                   out Float inverseWi, out Float inverseWo, out Float inverseH)
        {
            Vector3f toPrevious = previous.p - vertex.p, toNext = next.p - vertex.p;
            inverseWi = 1 / Length(toPrevious);
            inverseWo = 1 / Length(toNext);
            wi = toPrevious * inverseWi;
            wo = toNext * inverseWo;
            Vector3f h = wi + vertex.eta * wo;
            inverseH = 1 / Length(h);
            return h * inverseH;
        }

        // The tangents of a vertex made orthogonal to its shading normal
        static void Tangents(ManifoldVertex vertex, out Vector3f s, out Vector3f t)
        {
            Vector3f n = (Vector3f)vertex.n;
            s = vertex.dpdu - Dot(vertex.dpdu, n) * n;
            t = vertex.dpdv - Dot(vertex.dpdv, n) * n;
        }

        // The constraint of a specular vertex: its generalised half vector along the two tangents (zero on the manifold)
        public static Point2f Constraint(ManifoldVertex previous, ManifoldVertex vertex, ManifoldVertex next)
        {
            Vector3f h = HalfVector(previous, vertex, next, out Vector3f wi, out Vector3f wo, out Float inverseWi, out Float inverseWo,
                                    out Float inverseH);
            Tangents(vertex, out Vector3f s, out Vector3f t);
            return new Point2f(Dot(h, s), Dot(h, t));
        }

        // The Jacobian of the constraints of the specular vertices x[1] .. x[n] of a chain x[0] .. x[n + 1]: for vertex i (stored
        // at index i - 1), A, B and C are the derivatives of its constraint with respect to the (u, v) of x[i - 1], x[i], x[i + 1]
        public static void Jacobian(ManifoldVertex[] x, ManifoldMatrix[] A, ManifoldMatrix[] B, ManifoldMatrix[] C)
        {
            int n = x.Length - 2;
            for (int i = 1; i <= n; ++i)
            {
                ManifoldVertex previous = x[i - 1], vertex = x[i], next = x[i + 1];
                Vector3f h = HalfVector(previous, vertex, next, out Vector3f wi, out Vector3f wo, out Float inverseWi, out Float inverseWo,
                                        out Float inverseH);
                Tangents(vertex, out Vector3f s, out Vector3f t);
                Vector3f normal = (Vector3f)vertex.n;
                Float scaleWi = inverseWi * inverseH, scaleWo = inverseWo * inverseH * vertex.eta;

                // Derivative with respect to x[i - 1]
                {
                    Vector3f tu = (previous.dpdu - wi * Dot(wi, previous.dpdu)) * scaleWi;
                    Vector3f tv = (previous.dpdv - wi * Dot(wi, previous.dpdv)) * scaleWi;
                    Vector3f dHdu = tu - h * Dot(tu, h), dHdv = tv - h * Dot(tv, h);
                    A[i - 1] = new ManifoldMatrix(Dot(dHdu, s), Dot(dHdv, s), Dot(dHdu, t), Dot(dHdv, t));
                }

                // Derivative with respect to x[i]: the half vector turns and so do the tangents, with the normal
                {
                    Vector3f tu = -vertex.dpdu * (scaleWi + scaleWo) + wi * (Dot(wi, vertex.dpdu) * scaleWi) + wo * (Dot(wo, vertex.dpdu) * scaleWo);
                    Vector3f tv = -vertex.dpdv * (scaleWi + scaleWo) + wi * (Dot(wi, vertex.dpdv) * scaleWi) + wo * (Dot(wo, vertex.dpdv) * scaleWo);
                    Vector3f dHdu = tu - h * Dot(tu, h), dHdv = tv - h * Dot(tv, h);
                    Vector3f dndu = (Vector3f)vertex.dndu, dndv = (Vector3f)vertex.dndv;
                    Float hn = Dot(h, normal), hdndu = Dot(h, dndu), hdndv = Dot(h, dndv);
                    Float un = Dot(vertex.dpdu, normal), vn = Dot(vertex.dpdv, normal);
                    B[i - 1] = new ManifoldMatrix(
                        Dot(dHdu, s) - Dot(vertex.dpdu, dndu) * hn - un * hdndu, Dot(dHdv, s) - Dot(vertex.dpdu, dndv) * hn - un * hdndv,
                        Dot(dHdu, t) - Dot(vertex.dpdv, dndu) * hn - vn * hdndu, Dot(dHdv, t) - Dot(vertex.dpdv, dndv) * hn - vn * hdndv);
                }

                // Derivative with respect to x[i + 1]
                {
                    Vector3f tu = (next.dpdu - wo * Dot(wo, next.dpdu)) * scaleWo;
                    Vector3f tv = (next.dpdv - wo * Dot(wo, next.dpdv)) * scaleWo;
                    Vector3f dHdu = tu - h * Dot(tu, h), dHdv = tv - h * Dot(tv, h);
                    C[i - 1] = new ManifoldMatrix(Dot(dHdu, s), Dot(dHdv, s), Dot(dHdu, t), Dot(dHdv, t));
                }
            }
        }

        // Solves the block tridiagonal system A_i w_i-1 + B_i w_i + C_i w_i+1 = v_i for the n specular vertices (w_0 and w_n+1
        // are not unknowns: the caller moves them to the right-hand side). False if a block has no inverse.
        public static bool Solve(ManifoldMatrix[] A, ManifoldMatrix[] B, ManifoldMatrix[] C, Point2f[] v, Point2f[] w)
        {
            int n = B.Length;
            ManifoldMatrix[] lower = new ManifoldMatrix[n], upper = new ManifoldMatrix[n], upperInverse = new ManifoldMatrix[n];
            Point2f[] forward = new Point2f[n];
            upper[0] = B[0];
            if (!upper[0].Inverse(out upperInverse[0]))
            {
                return false;
            }
            forward[0] = v[0];
            for (int i = 1; i < n; ++i)
            {
                lower[i] = A[i] * upperInverse[i - 1];
                upper[i] = B[i] - lower[i] * C[i - 1];
                if (!upper[i].Inverse(out upperInverse[i]))
                {
                    return false;
                }
                Point2f carried = lower[i] * forward[i - 1];
                forward[i] = new Point2f(v[i].x - carried.x, v[i].y - carried.y);
            }
            w[n - 1] = upperInverse[n - 1] * forward[n - 1];
            for (int i = n - 2; i >= 0; --i)
            {
                Point2f carried = C[i] * w[i + 1];
                w[i] = upperInverse[i] * new Point2f(forward[i].x - carried.x, forward[i].y - carried.y);
            }
            return true;
        }

        // The offsets (u, v) of the specular vertices that keep the chain on the manifold when its last vertex moves by (du, dv)
        // along its tangents. False if the Jacobian has no inverse.
        public static bool Offsets(ManifoldVertex[] x, Point2f endOffset, Point2f[] offsets)
        {
            int n = x.Length - 2;
            ManifoldMatrix[] A = new ManifoldMatrix[n], B = new ManifoldMatrix[n], C = new ManifoldMatrix[n];
            Jacobian(x, A, B, C);
            Point2f[] v = new Point2f[n];
            Point2f moved = C[n - 1] * endOffset;
            v[n - 1] = new Point2f(-moved.x, -moved.y);
            return Solve(A, B, C, v, offsets);
        }

        // |det(d x_1 / d x_n+1)|: how the area about the first specular vertex scales with the area about the last vertex of
        // the chain, in the (u, v) of both, turned into areas by the lengths of their tangents. Zero if the Jacobian has no inverse.
        public static Float AreaRatio(ManifoldVertex[] x)
        {
            int n = x.Length - 2;
            // The first vertex's offsets for a unit move of the end along each of its tangents
            Point2f[] alongU = new Point2f[n], alongV = new Point2f[n];
            if (!Offsets(x, new Point2f(1, 0), alongU) || !Offsets(x, new Point2f(0, 1), alongV))
            {
                return 0;
            }
            Float determinant = alongU[0].x * alongV[0].y - alongU[0].y * alongV[0].x;
            Float firstArea = Length(Cross(x[1].dpdu, x[1].dpdv)), lastArea = Length(Cross(x[n + 1].dpdu, x[n + 1].dpdv));
            if (lastArea == 0)
            {
                return 0;
            }
            return std.abs(determinant) * firstArea / lastArea;
        }

        // The generalised geometric term between the endpoints of a chain: that of its first segment times the area ratio. The
        // cosines are with the normals of the two endpoints of that segment (x[0].n and x[1].n).
        public static Float GeneralisedG(ManifoldVertex[] x)
        {
            Vector3f d = x[1].p - x[0].p;
            Float distanceSquared = LengthSquared(d);
            if (distanceSquared == 0)
            {
                return 0;
            }
            Vector3f w = d / std.sqrt(distanceSquared);
            return AbsDot(x[0].n, w) * AbsDot(x[1].n, w) / distanceSquared * AreaRatio(x);
        }

        // The direction in which a chain leaves a specular vertex that it reaches along -wi (wi points back along the chain).
        // False at a total internal reflection.
        public static bool SpecularDirection(ManifoldVertex vertex, Vector3f wi, out Vector3f wo)
        {
            Normal3f n = vertex.n;
            if (vertex.reflection)
            {
                wo = Reflect(wi, (Vector3f)n);
                return true;
            }
            // (Refract() takes the index below the normal over the index above it, and turns it over for a direction that
            // comes from below: vertex.eta is the index of the side the chain goes to over that of the side it comes from)
            Float etap;
            Vector3f wt;
            Float eta = Dot(wi, (Vector3f)n) > 0 ? vertex.eta : 1 / vertex.eta;
            if (!Refract(wi, n, eta, &etap, &wt))
            {
                wo = default;
                return false;
            }
            wo = wt;
            return true;
        }

        // The nearest surface along a ray that leaves a point of a chain (`from` is the surface it leaves, null at the start);
        // null if there is none
        public delegate SurfaceInteraction Tracer(SurfaceInteraction from, Point3f origin, Vector3f direction);

        // Traces a chain from x[0] toward a point: the specular vertices are where the rays land, with the kind and the index of
        // those of x, and the last vertex is where the last ray lands or, with an end plane (a vertex with a position, a normal
        // and two tangents), where it crosses that plane. Null if a ray misses or is totally reflected.
        public static ManifoldVertex[] TraceChain(ManifoldVertex[] x, Point3f toward, Tracer trace, SurfaceInteraction[] hits,
                                                  ManifoldVertex endPlane)
        {
            int n = x.Length - 2;
            ManifoldVertex[] chain = new ManifoldVertex[n + 2];
            chain[0] = x[0];
            Point3f origin = x[0].p;
            Vector3f direction = toward - origin;
            if (LengthSquared(direction) == 0)
            {
                return null;
            }
            direction = Normalize(direction);
            SurfaceInteraction from = null;
            for (int i = 1; i <= n + 1; ++i)
            {
                if (i == n + 1 && endPlane != null)
                {
                    Float along = Dot(direction, (Vector3f)endPlane.n);
                    if (std.abs(along) < 1e-6f)
                    {
                        return null;
                    }
                    Float t = Dot(endPlane.p - origin, (Vector3f)endPlane.n) / along;
                    if (!(t > 0))
                    {
                        return null;
                    }
                    ManifoldVertex end = new ManifoldVertex();
                    end.p = origin + t * direction;
                    end.dpdu = endPlane.dpdu;
                    end.dpdv = endPlane.dpdv;
                    end.n = endPlane.n;
                    chain[i] = end;
                    if (hits != null)
                    {
                        hits[i] = null;
                    }
                    break;
                }
                SurfaceInteraction hit = trace(from, origin, direction);
                if (hit == null)
                {
                    return null;
                }
                ManifoldVertex vertex = ManifoldVertex.Of(hit);
                if (hits != null)
                {
                    hits[i] = hit;
                }
                chain[i] = vertex;
                if (i <= n)
                {
                    vertex.eta = x[i].eta;
                    vertex.reflection = x[i].reflection;
                    if (!SpecularDirection(vertex, -direction, out Vector3f next))
                    {
                        return null;
                    }
                    direction = next;
                }
                origin = hit.p();
                from = hit;
            }
            return chain;
        }

        // The walk on the manifold: from a chain x that satisfies the constraints, the chain with the same first vertex and the
        // same kinds of specular vertices whose last vertex is at the target. Null if the walk does not get there. `hits` (when
        // not null, n + 2 long) gets the surfaces of the vertices 1 .. n + 1 of the result. With an end plane (through the target)
        // the last vertex moves on that plane and its segment is not traced: the target can then be anywhere, in a medium too.
        public static ManifoldVertex[] Walk(ManifoldVertex[] x, Point3f target, Tracer trace, SurfaceInteraction[] hits = null,
                                            ManifoldVertex endPlane = null)
        {
            const int MaximumIterations = 50;
            const Float Tolerance = 1e-4f;
            int n = x.Length - 2;
            ManifoldVertex[] current = x;
            SurfaceInteraction[] proposedHits = hits != null ? new SurfaceInteraction[n + 2] : null;
            if (endPlane != null)
            {
                // (the chain as it is, with its end on the plane)
                current = TraceChain(x, x[1].p, trace, hits, endPlane);
                if (current == null)
                {
                    return null;
                }
            }
            Float beta = 1;
            Point2f[] offsets = new Point2f[n];
            for (int iteration = 0; iteration < MaximumIterations; ++iteration)
            {
                // The size of the scene of the chain, for the tolerance
                Float size = 0;
                for (int i = 0; i < n + 2; ++i)
                {
                    size = std.max<Float>(size, Length((Vector3f)current[i].p));
                }
                ManifoldVertex end = current[n + 1];
                Vector3f toTarget = target - end.p;
                Float distance = Length(toTarget);
                if (distance < Tolerance * std.max<Float>(size, 1e-3f))
                {
                    return current;
                }

                // The move of the end in its own (u, v), by least squares
                Float uu = Dot(end.dpdu, end.dpdu), uv = Dot(end.dpdu, end.dpdv), vv = Dot(end.dpdv, end.dpdv);
                Float determinant = uu * vv - uv * uv;
                if (determinant == 0)
                {
                    return null;
                }
                Float du = (vv * Dot(end.dpdu, toTarget) - uv * Dot(end.dpdv, toTarget)) / determinant;
                Float dv = (uu * Dot(end.dpdv, toTarget) - uv * Dot(end.dpdu, toTarget)) / determinant;
                if (!Offsets(current, new Point2f(du, dv), offsets))
                {
                    return null;
                }

                // Move the first specular vertex by a step, and trace the chain through it
                Point3f toward = current[1].p + beta * (current[1].dpdu * offsets[0].x + current[1].dpdv * offsets[0].y);
                ManifoldVertex[] proposed = TraceChain(current, toward, trace, proposedHits, endPlane);
                if (proposed != null && Length(target - proposed[n + 1].p) < distance)
                {
                    current = proposed;
                    if (hits != null)
                    {
                        for (int i = 1; i <= n + 1; ++i)
                        {
                            hits[i] = proposedHits[i];
                        }
                    }
                    beta = std.min<Float>(1, 2 * beta);
                }
                else
                {
                    beta *= 0.5f;
                    if (beta < 1e-6f)
                    {
                        return null;
                    }
                }
            }
            return null;
        }
        // Half vector space (Kaplanyan et al. 2014, Hanika et al. 2015). A chain x[0] .. x[n + 1] with fixed ends is given by
        // the half vectors of its inner vertices: unit vectors of the world, so that no field of tangent frames comes into the
        // change of variables. A specular vertex holds its half vector to its normal; any other vertex holds it to a target.

        // The half vector of an inner vertex of a chain
        public static Vector3f HalfVector(ManifoldVertex previous, ManifoldVertex vertex, ManifoldVertex next)
        {
            return HalfVector(previous, vertex, next, out Vector3f wi, out Vector3f wo, out Float inverseWi, out Float inverseWo, out Float inverseH);
        }

        // What is left of the constraints of a chain: for every inner vertex the direction that its half vector is held to
        // (its normal, or its target) less the half vector, along the two axes across the half vector. Returns the largest.
        public static double HalfVectorResidual(ManifoldVertex[] x, Vector3f[] targets, double[] residual)
        {
            int n = x.Length - 2;
            double largest = 0;
            for (int i = 1; i <= n; ++i)
            {
                Vector3f h = HalfVector(x[i - 1], x[i], x[i + 1]);
                Vector3f held = targets[i];
                if (x[i].specular)
                {
                    Vector3f normal = (Vector3f)x[i].n;
                    held = Dot(h, normal) < 0 ? -normal : normal;
                }
                Vector3f b1, b2;
                CoordinateSystem(h, &b1, &b2);
                residual[2 * (i - 1)] = Dot(held - h, b1);
                residual[2 * (i - 1) + 1] = Dot(held - h, b2);
                largest = Math.Max(largest, Math.Max(Math.Abs(residual[2 * (i - 1)]), Math.Abs(residual[2 * (i - 1) + 1])));
            }
            return largest;
        }

        // The derivatives of the constraints of HalfVectorResidual() (the half vector less what it is held to) with respect to
        // the (u, v) of the inner vertices: a matrix of 2n rows and columns, row by row
        public static void HalfVectorJacobian(ManifoldVertex[] x, double[] J)
        {
            int n = x.Length - 2, size = 2 * n;
            Array.Clear(J, 0, size * size);
            for (int i = 1; i <= n; ++i)
            {
                ManifoldVertex previous = x[i - 1], vertex = x[i], next = x[i + 1];
                Vector3f h = HalfVector(previous, vertex, next, out Vector3f wi, out Vector3f wo, out Float inverseWi, out Float inverseWo,
                                        out Float inverseH);
                Vector3f b1, b2;
                CoordinateSystem(h, &b1, &b2);
                Float scaleWi = inverseWi * inverseH, scaleWo = inverseWo * inverseH * vertex.eta;
                int row = 2 * (i - 1);
                for (int axis = 0; axis < 2; ++axis)
                {
                    if (i > 1)
                    {
                        Vector3f tangent = axis == 0 ? previous.dpdu : previous.dpdv;
                        Vector3f t = (tangent - wi * Dot(wi, tangent)) * scaleWi;
                        Vector3f dH = t - h * Dot(t, h);
                        J[row * size + row - 2 + axis] = Dot(dH, b1);
                        J[(row + 1) * size + row - 2 + axis] = Dot(dH, b2);
                    }
                    {
                        Vector3f tangent = axis == 0 ? vertex.dpdu : vertex.dpdv;
                        Vector3f t = -tangent * (scaleWi + scaleWo) + wi * (Dot(wi, tangent) * scaleWi) + wo * (Dot(wo, tangent) * scaleWo);
                        Vector3f dH = t - h * Dot(t, h);
                        if (vertex.specular)
                        {
                            // (the normal that the half vector is held to turns with the vertex)
                            Vector3f normal = (Vector3f)vertex.n, dn = (Vector3f)(axis == 0 ? vertex.dndu : vertex.dndv);
                            dn -= normal * Dot(normal, dn);
                            dH -= Dot(h, normal) < 0 ? -dn : dn;
                        }
                        J[row * size + row + axis] = Dot(dH, b1);
                        J[(row + 1) * size + row + axis] = Dot(dH, b2);
                    }
                    if (i < n)
                    {
                        Vector3f tangent = axis == 0 ? next.dpdu : next.dpdv;
                        Vector3f t = (tangent - wo * Dot(wo, tangent)) * scaleWo;
                        Vector3f dH = t - h * Dot(t, h);
                        J[row * size + row + 2 + axis] = Dot(dH, b1);
                        J[(row + 1) * size + row + 2 + axis] = Dot(dH, b2);
                    }
                }
            }
        }

        // Factors a square matrix in place (rows exchanged for the largest pivot) and returns the logarithm of the absolute value
        // of its determinant. False if it has no inverse.
        public static bool Factor(double[] M, int size, int[] pivots, out double logDeterminant)
        {
            logDeterminant = 0;
            for (int column = 0; column < size; ++column)
            {
                int pivot = column;
                for (int row = column + 1; row < size; ++row)
                {
                    if (Math.Abs(M[row * size + column]) > Math.Abs(M[pivot * size + column]))
                    {
                        pivot = row;
                    }
                }
                pivots[column] = pivot;
                if (pivot != column)
                {
                    for (int j = 0; j < size; ++j)
                    {
                        double swapped = M[column * size + j];
                        M[column * size + j] = M[pivot * size + j];
                        M[pivot * size + j] = swapped;
                    }
                }
                double diagonal = M[column * size + column];
                if (diagonal == 0 || double.IsNaN(diagonal))
                {
                    return false;
                }
                logDeterminant += Math.Log(Math.Abs(diagonal));
                for (int row = column + 1; row < size; ++row)
                {
                    double factor = M[row * size + column] / diagonal;
                    M[row * size + column] = factor;
                    for (int j = column + 1; j < size; ++j)
                    {
                        M[row * size + j] -= factor * M[column * size + j];
                    }
                }
            }
            return true;
        }

        // Solves M w = v in place of v, for a matrix that Factor() has factored
        public static void Substitute(double[] M, int size, int[] pivots, double[] v)
        {
            for (int column = 0; column < size; ++column)
            {
                double swapped = v[column];
                v[column] = v[pivots[column]];
                v[pivots[column]] = swapped;
                for (int row = column + 1; row < size; ++row)
                {
                    v[row] -= M[row * size + column] * v[column];
                }
            }
            for (int row = size - 1; row >= 0; --row)
            {
                for (int j = row + 1; j < size; ++j)
                {
                    v[row] -= M[row * size + j] * v[j];
                }
                v[row] /= M[row * size + row];
            }
        }

        // The solid angles of the half vectors of the vertices of a chain that are not specular over the areas of those
        // vertices, the specular vertices following on their manifold: the change of variables between the vertices of the
        // chain and its half vectors. Zero if the constraints have no inverse.
        public static double HalfVectorMeasure(ManifoldVertex[] x)
        {
            int n = x.Length - 2, size = 2 * n;
            double[] J = new double[size * size];
            HalfVectorJacobian(x, J);
            int specular = 0;
            double logAreas = 0;
            for (int i = 1; i <= n; ++i)
            {
                if (x[i].specular)
                {
                    ++specular;
                }
                else
                {
                    logAreas += Math.Log(Length(Cross(x[i].dpdu, x[i].dpdv)));
                }
            }
            // The block of the specular vertices: the determinant of the whole over that of the block is the determinant of the
            // rest once the specular vertices are solved for
            double logSpecular = 0;
            if (specular > 0)
            {
                int blockSize = 2 * specular;
                double[] block = new double[blockSize * blockSize];
                int blockRow = 0;
                for (int i = 1; i <= n; ++i)
                {
                    if (!x[i].specular)
                    {
                        continue;
                    }
                    int blockColumn = 0;
                    for (int j = 1; j <= n; ++j)
                    {
                        if (!x[j].specular)
                        {
                            continue;
                        }
                        for (int r = 0; r < 2; ++r)
                        {
                            for (int c = 0; c < 2; ++c)
                            {
                                block[(2 * blockRow + r) * blockSize + 2 * blockColumn + c] = J[(2 * (i - 1) + r) * size + 2 * (j - 1) + c];
                            }
                        }
                        ++blockColumn;
                    }
                    ++blockRow;
                }
                if (!Factor(block, blockSize, new int[blockSize], out logSpecular))
                {
                    return 0;
                }
            }
            if (!Factor(J, size, new int[size], out double logWhole))
            {
                return 0;
            }
            return Math.Exp(logWhole - logSpecular - logAreas);
        }

        // The steps of the half vectors of a chain that move a point of the film evenly (Hanika et al. 2015, section 3: the ray
        // differentials of half vector space). `adjacent` is the inner vertex next to the camera and P the derivative of the
        // point on the film with respect to its (u, v). For every vertex i that is not specular, steps[i] is a matrix S such
        // that S g, g a pair of unit Gaussians, is a step of its half vector (along the two axes across it, CoordinateSystem())
        // that moves the point on the film by about pixels / sqrt(m) pixels in any direction, m being the number of such
        // vertices. The matrix that does so exactly is the inverse of d(film) / d(h_i) = P D_i, D_i the block of the inverse
        // Jacobian; its singular values are clipped to [smallest, largest] after the decomposition, which keeps its axes
        // (the paper's figure 5d). False if the Jacobian has no inverse.
        public static bool HalfVectorSteps(ManifoldVertex[] x, int adjacent, ManifoldMatrix P, Float pixels, Float smallest, Float largest,
                                           ManifoldMatrix[] steps)
        {
            int n = x.Length - 2, size = 2 * n;
            double[] J = new double[size * size], column = new double[size];
            int[] pivots = new int[size];
            HalfVectorJacobian(x, J);
            if (!Factor(J, size, pivots, out double unused))
            {
                return false;
            }
            int free = 0;
            for (int i = 1; i <= n; ++i)
            {
                free += x[i].specular ? 0 : 1;
            }
            Float scale = pixels / std.sqrt((Float)Math.Max(free, 1));
            for (int i = 1; i <= n; ++i)
            {
                if (x[i].specular)
                {
                    continue;
                }
                // D_i: how the vertex next to the camera moves with the half vector of vertex i
                ManifoldMatrix D = default;
                for (int axis = 0; axis < 2; ++axis)
                {
                    Array.Clear(column, 0, size);
                    column[2 * (i - 1) + axis] = 1;
                    Substitute(J, size, pivots, column);
                    Float du = (Float)column[2 * (adjacent - 1)], dv = (Float)column[2 * (adjacent - 1) + 1];
                    if (axis == 0)
                    {
                        D.m00 = du;
                        D.m10 = dv;
                    }
                    else
                    {
                        D.m01 = du;
                        D.m11 = dv;
                    }
                }
                ManifoldMatrix E = P * D;
                Float c1 = largest, c2 = largest, angle = 0;
                if (E.Inverse(out ManifoldMatrix M) && !Float.IsNaN(M.m00) && !Float.IsInfinity(M.m00))
                {
                    // The singular values and the left axes of M: the eigenvalues and eigenvectors of M M^T
                    Float a00 = M.m00 * M.m00 + M.m01 * M.m01, a01 = M.m00 * M.m10 + M.m01 * M.m11, a11 = M.m10 * M.m10 + M.m11 * M.m11;
                    Float mean = (a00 + a11) / 2, spread = std.sqrt(Sqr((a00 - a11) / 2) + Sqr(a01));
                    angle = std.atan2(2 * a01, a00 - a11) / 2;
                    c1 = Clamp(scale * SafeSqrt(mean + spread), smallest, largest);
                    c2 = Clamp(scale * SafeSqrt(mean - spread), smallest, largest);
                    if (Float.IsNaN(c1) || Float.IsNaN(c2) || Float.IsNaN(angle))
                    {
                        c1 = largest;
                        c2 = largest;
                        angle = 0;
                    }
                }
                Float cosAngle = std.cos(angle), sinAngle = std.sin(angle);
                steps[i] = new ManifoldMatrix(cosAngle * c1, -sinAngle * c2, sinAngle * c1, cosAngle * c2);
            }
            return true;
        }

        // The logarithm of the density (up to 2 pi) of the step t = S g of a pair of unit Gaussians g
        public static double LogStepDensity(ManifoldMatrix S, Point2f t)
        {
            if (!S.Inverse(out ManifoldMatrix inverse))
            {
                return double.NegativeInfinity;
            }
            Point2f g = inverse * t;
            return -0.5 * ((double)g.x * g.x + (double)g.y * g.y) - Math.Log(Math.Abs(S.Determinant()));
        }

        // Traces a chain through the points that a step predicts for its inner vertices: every vertex is where the ray from
        // the vertex before it lands, sent toward the predicted point or, after a specular vertex, along its specular direction.
        // The ends stay. Null if a ray misses.
        static ManifoldVertex[] TraceThrough(ManifoldVertex[] x, Point3f[] predicted, Tracer trace, SurfaceInteraction[] hits)
        {
            int n = x.Length - 2;
            ManifoldVertex[] chain = new ManifoldVertex[n + 2];
            chain[0] = x[0];
            chain[n + 1] = x[n + 1];
            SurfaceInteraction from = null;
            Vector3f direction = default;
            for (int i = 1; i <= n; ++i)
            {
                Vector3f next = default;
                if (i > 1 && chain[i - 1].specular)
                {
                    if (!SpecularDirection(chain[i - 1], -direction, out next))
                    {
                        return null;
                    }
                }
                else
                {
                    next = predicted[i] - chain[i - 1].p;
                    if (LengthSquared(next) == 0)
                    {
                        return null;
                    }
                    next = Normalize(next);
                }
                direction = next;
                SurfaceInteraction hit = trace(from, chain[i - 1].p, direction);
                if (hit == null)
                {
                    return null;
                }
                ManifoldVertex vertex = ManifoldVertex.Of(hit);
                vertex.eta = x[i].eta;
                vertex.reflection = x[i].reflection;
                vertex.specular = x[i].specular;
                chain[i] = vertex;
                if (hits != null)
                {
                    hits[i] = hit;
                }
                from = hit;
            }
            return chain;
        }

        // The chain with the ends of x whose specular vertices hold their half vectors to their normals and whose other inner
        // vertices have the target half vectors: Newton's method from x, every step traced (a step that does not bring the
        // constraints closer is halved). Null if it does not get there.
        public static ManifoldVertex[] SolveHalfVectors(ManifoldVertex[] x, Vector3f[] targets, Tracer trace, SurfaceInteraction[] hits)
        {
            const int MaximumIterations = 50;
            const double Tolerance = 2e-5;
            int n = x.Length - 2, size = 2 * n;
            double[] J = new double[size * size], residual = new double[size], proposedResidual = new double[size];
            int[] pivots = new int[size];
            Point3f[] predicted = new Point3f[n + 2];
            SurfaceInteraction[] proposedHits = hits != null ? new SurfaceInteraction[n + 2] : null;
            // (the chain as traced: after a specular vertex the chain follows its direction exactly)
            for (int i = 1; i <= n; ++i)
            {
                predicted[i] = x[i].p;
            }
            ManifoldVertex[] current = TraceThrough(x, predicted, trace, hits);
            if (current == null)
            {
                return null;
            }
            double largest = HalfVectorResidual(current, targets, residual);
            Float beta = 1;
            for (int iteration = 0; iteration < MaximumIterations; ++iteration)
            {
                if (largest < Tolerance)
                {
                    return current;
                }
                HalfVectorJacobian(current, J);
                if (!Factor(J, size, pivots, out double unused))
                {
                    return null;
                }
                Substitute(J, size, pivots, residual);
                for (int i = 1; i <= n; ++i)
                {
                    predicted[i] = current[i].p + beta * ((Float)residual[2 * (i - 1)] * current[i].dpdu + (Float)residual[2 * (i - 1) + 1] * current[i].dpdv);
                }
                ManifoldVertex[] proposed = TraceThrough(current, predicted, trace, proposedHits);
                double proposedLargest = proposed != null ? HalfVectorResidual(proposed, targets, proposedResidual) : 0;
                if (proposed != null && proposedLargest < largest)
                {
                    current = proposed;
                    largest = proposedLargest;
                    if (hits != null)
                    {
                        for (int i = 1; i <= n; ++i)
                        {
                            hits[i] = proposedHits[i];
                        }
                    }
                    beta = std.min<Float>(1, 2 * beta);
                }
                else
                {
                    beta *= 0.5f;
                    if (beta < 1e-5f)
                    {
                        return null;
                    }
                }
                HalfVectorResidual(current, targets, residual);
            }
            return largest < Tolerance ? current : null;
        }
    }

}  // namespace pbrt
