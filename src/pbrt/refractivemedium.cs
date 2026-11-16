// (not in pbrt-v4) refractivemedium.cs
//
// A medium whose index of refraction varies, as one more volume of the production framework (productionvolume.cs).
//
// RefractiveMedium holds an ordinary medium, which gives the coefficients, the emission and the phase function, and a
// TetrahedralIndexField (refractivefield.cs), which bends the light paths: a path through the field is a chain of
// parabolas, exactly. RefractiveVolume is its volume.
//
// The trackers and the transmittance estimators are those of every other volume, unchanged. A curved path has the
// parameter sigma, with ds = n dsigma for the arc length s; the optical depth along it is the integral of mu n dsigma. So
// the path is shown to them as a straight ray whose distance is sigma (CurvedRayMedium), through a medium whose
// coefficients are those of the held medium times the index, at the point of the curve for that sigma. A majorant of a
// piece is that of the held medium over the piece times the largest index of the piece. What they compute is then exactly
// what it would be along the curve.
//
// Radiance over the square of the index is what is constant along a path where nothing is lost, so the weight of what is
// found at the far end of a piece of a path, from the near end, carries (n_near / n_far)^2.
//
// A ray that the field does not bend (it is the same everywhere along it) is given to the volume of the held medium as it
// is. For one that it bends, the volume says so (VolumeContext.bent, Volume.Transmit), and the path tracer goes on with
// the ray that the volume returns.

using System;
using System.Collections.Generic;

namespace pbrt
{
    using static pbrt;

    // RefractiveMedium Definition
    public sealed unsafe class RefractiveMedium : Medium, IMajorantMedium<RayMajorantIterator>
    {
        public RefractiveMedium(Medium inner, TetrahedralIndexField field, bool surfacesInside)
        {
            this.inner = inner;
            this.field = field;
            this.surfacesInside = surfacesInside;
        }

        // "string medium": the type of the medium that is held, made from the same parameters.
        // "string indexfield" ("grid" | "bump" | "luneburg"):
        //     grid: "float index" [...] with "integer inx" "integer iny" "integer inz" samples, interpolated, on a lattice
        //         "integer refine" (2) times as fine; the index is "float indexoffset" (0) + "float indexscale" (1) x sample;
        //     bump: n = 1 + "float indexamplitude" (1 - (r / R)^2)^3 within "float indexradius" R of "point3 indexcenter";
        //     luneburg: n = sqrt(2 - (r / R)^2) within R, 1 outside;
        //     the last two on a lattice of "integer indexcells" (32) cells each way.
        // "point3 ip0", "point3 ip1": the box of the field (those of "p0" and "p1" otherwise).
        // "bool surfacesinside" (true): whether a path is tested against the surfaces along the way (off: only where it
        //     leaves the field, for a medium that holds no surface and is bounded by the box of the field or a larger shape).
        public static RefractiveMedium Create(ParameterDictionary parameters, Transform renderFromMedium, FileLoc loc, Allocator alloc)
        {
            return Create(parameters, renderFromMedium, loc, alloc, null);
        }

        // (`held`: a medium that was made before, named by "string heldmedium" -- an aggregate, say -- in the place of one
        // made here from "string medium")
        public static RefractiveMedium Create(ParameterDictionary parameters, Transform renderFromMedium, FileLoc loc, Allocator alloc, Medium held)
        {
            string innerName = held != null ? "heldmedium" : parameters.GetOneString("medium", "homogeneous");
            if (innerName == "refractive")
                ErrorExit(loc, "\"refractive\": the medium that it holds cannot be \"refractive\".");
            string kind = parameters.GetOneString("indexfield", "grid");
            Point3f p0 = parameters.GetOnePoint3f("ip0", parameters.GetOnePoint3f("p0", new Point3f(0, 0, 0)));
            Point3f p1 = parameters.GetOnePoint3f("ip1", parameters.GetOnePoint3f("p1", new Point3f(1, 1, 1)));
            Bounds3f box = renderFromMedium.Apply(new Bounds3f(p0, p1));
            bool surfacesInside = parameters.GetOneBool("surfacesinside", true);
            TetrahedralIndexField field;
            if (kind == "grid")
            {
                List<Float> index = parameters.GetFloatArray("index");
                int nx = parameters.GetOneInt("inx", 1), ny = parameters.GetOneInt("iny", 1), nz = parameters.GetOneInt("inz", 1);
                int refine = parameters.GetOneInt("refine", 2);
                if (index.Count == 0 || index.Count != nx * ny * nz)
                    ErrorExit(loc, "\"refractive\": \"index\" must have inx * iny * inz values.");
                if (refine < 1)
                    ErrorExit(loc, "\"refractive\": \"refine\" must be at least 1.");
                field = TetrahedralIndexField.FromGrid(box, index.ToArray(), nx, ny, nz, refine, parameters.GetOneFloat("indexoffset", 0),
                                                       parameters.GetOneFloat("indexscale", 1));
            }
            else if (kind == "bump" || kind == "luneburg")
            {
                Point3f center = renderFromMedium.Apply(parameters.GetOnePoint3f("indexcenter", new Point3f(0.5f * (p0.x + p1.x), 0.5f * (p0.y + p1.y), 0.5f * (p0.z + p1.z))));
                Float radius = parameters.GetOneFloat("indexradius", 1);
                Float amplitude = parameters.GetOneFloat("indexamplitude", 0.1f);
                int cells = parameters.GetOneInt("indexcells", 32);
                if (!(radius > 0) || cells < 1)
                    ErrorExit(loc, "\"refractive\": \"indexradius\" must be positive and \"indexcells\" at least 1.");
                bool bump = kind == "bump";
                field = new TetrahedralIndexField(box, cells, cells, cells, (Point3f p) =>
                {
                    double r2 = (double)DistanceSquared(p, center) / ((double)radius * radius);
                    if (r2 >= 1)
                    {
                        return 1.0;
                    }
                    if (bump)
                    {
                        double inside = 1 - r2;
                        return 1 + amplitude * inside * inside * inside;
                    }
                    return Math.Sqrt(2 - r2);
                });
            }
            else
            {
                ErrorExit(loc, $"{kind}: \"indexfield\" must be \"grid\", \"bump\" or \"luneburg\".");
                return null;
            }
            Medium inner = held != null ? held : Medium.Create(innerName, parameters, renderFromMedium, loc, alloc);
            if (inner.emissionLight != null)
                ErrorExit(loc, "\"refractive\": the sampling of the emission (\"emission_nee\", \"emission_fnee\") is not for a refractive medium yet.");
            SampledSpectrum bound;
            Float control, average;
            if (!inner.MajorantOver(box, SampledWavelengths.SampleUniform(0.5f), out bound, out control, out average))
                ErrorExit(loc, $"{innerName}: a refractive medium can hold \"homogeneous\", \"uniformgrid\", \"adaptivegrid\" and aggregates of those.");
            return new RefractiveMedium(inner, field, surfacesInside);
        }

        public override string ToString()
        {
            return StringPrintf("[ RefractiveMedium inner: %s ]", inner);
        }

        public override bool IsEmissive() { return inner.IsEmissive(); }

        public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda)
        {
            return inner.SamplePoint(p, lambda);
        }

        public RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda)
        {
            return inner.SampleRay(ray, tMax, lambda, new ScratchBuffer());
        }

        public override RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda, ScratchBuffer buf)
        {
            return inner.SampleRay(ray, tMax, lambda, buf);
        }

        public Medium inner;
        public TetrahedralIndexField field;
        public bool surfacesInside;
    }

    // CurvedPath Definition: a light path through a refractive medium from a point in a direction, to the first surface
    // or out of the scene
    public sealed class CurvedPath
    {
        public List<RefractivePiece> pieces = new List<RefractivePiece>();
        // Whether any piece is not straight
        public bool bent;
        // Where the path ends: on a surface (with the ray that reaches it and the distance along that ray), or nowhere
        public bool hit;
        public VolumeGeometry surface;
        public Point3f P;
        public Ray arrival;
        // The index at the start and at the end
        public double indexAtStart, indexAtEnd;
        // The sum of sigma over the pieces (infinite for a path that leaves the scene)
        public double sigma;
        // A path that did not come out of the field
        public bool lost;
        // A path that the plane of VolumeContext.stopAtPlane stopped (P is the point of the plane)
        public bool atPlane;
        // Whether the first piece is the straight way up to the field, and the last the straight way beyond it
        public bool lead, beyond;

        public void Reset()
        {
            pieces.Clear();
            bent = hit = lost = atPlane = lead = beyond = false;
            surface = null;
            P = default;
            arrival = default;
            indexAtStart = indexAtEnd = sigma = 0;
        }

        // A small change of the ray at the start of the path -- of its point (`offset`) and of its direction (`turn`, across
        // the direction) -- carried to the end of the path, in closed form: within a piece the change of the point grows
        // by sigma times the change of v; at the face between two pieces the changed ray crosses a little later, which adds
        // (A_before - A_after) times that delay to the change of v (the difference of the two is along the normal of the
        // face, the index being continuous); at the faces of the box of the field the direction is kept and v = n d.
        // The changes at the end compare the rays at the same parameter: what is along the ray there is of no account.
        public void Carry(Bounds3f box, ref Vector3f offset, ref Vector3f turn)
        {
            double px = offset.x, py = offset.y, pz = offset.z, qx = turn.x, qy = turn.y, qz = turn.z;
            bool inside = false;
            for (int k = 0; k < pieces.Count; ++k)
            {
                RefractivePiece piece = pieces[k];
                double n = Math.Sqrt(piece.vx * piece.vx + piece.vy * piece.vy + piece.vz * piece.vz);
                double dx = piece.vx / n, dy = piece.vy / n, dz = piece.vz / n;
                bool straight = (k == 0 && lead) || (k == pieces.Count - 1 && beyond);
                if (straight)
                {
                    if (inside)
                    {
                        // Out of the field: to the face, then to the direction
                        RefractivePiece before = pieces[k - 1];
                        double nx, ny, nz;
                        FaceNormal(box, piece.x, piece.y, piece.z, out nx, out ny, out nz);
                        double rate = nx * piece.vx + ny * piece.vy + nz * piece.vz;
                        double delay = rate != 0 ? -(nx * px + ny * py + nz * pz) / rate : 0;
                        px += piece.vx * delay;
                        py += piece.vy * delay;
                        pz += piece.vz * delay;
                        qx += before.ax * delay;
                        qy += before.ay * delay;
                        qz += before.az * delay;
                        double along = dx * qx + dy * qy + dz * qz;
                        qx = (qx - dx * along) / n;
                        qy = (qy - dy * along) / n;
                        qz = (qz - dz * along) / n;
                        inside = false;
                    }
                    if (!double.IsInfinity(piece.sigma))
                    {
                        double distance = piece.sigma * n;
                        px += qx * distance;
                        py += qy * distance;
                        pz += qz * distance;
                    }
                    continue;
                }
                if (!inside)
                {
                    // Into the field (or a start inside it): v = n(x) d
                    double delay = 0;
                    if (k > 0)
                    {
                        double nx, ny, nz;
                        FaceNormal(box, piece.x, piece.y, piece.z, out nx, out ny, out nz);
                        double rate = nx * dx + ny * dy + nz * dz;
                        delay = rate != 0 ? -(nx * px + ny * py + nz * pz) / rate : 0;
                    }
                    double fx = px + dx * delay, fy = py + dy * delay, fz = pz + dz * delay;
                    double gradient = (piece.ax * fx + piece.ay * fy + piece.az * fz) / n;
                    qx = n * qx + dx * gradient - piece.ax * delay / n;
                    qy = n * qy + dy * gradient - piece.ay * delay / n;
                    qz = n * qz + dz * gradient - piece.az * delay / n;
                    inside = true;
                }
                else
                {
                    RefractivePiece before = pieces[k - 1];
                    double jx = before.ax - piece.ax, jy = before.ay - piece.ay, jz = before.az - piece.az;
                    double rate = jx * piece.vx + jy * piece.vy + jz * piece.vz;
                    double size = Math.Sqrt(jx * jx + jy * jy + jz * jz);
                    if (Math.Abs(rate) > 1e-9 * size * n)
                    {
                        double delay = -(jx * px + jy * py + jz * pz) / rate;
                        qx += jx * delay;
                        qy += jy * delay;
                        qz += jz * delay;
                    }
                }
                px += qx * piece.sigma;
                py += qy * piece.sigma;
                pz += qz * piece.sigma;
            }
            if (inside)
            {
                // An end inside the field: to the direction there
                RefractivePiece last = pieces[pieces.Count - 1];
                double vx = last.vx + last.sigma * last.ax, vy = last.vy + last.sigma * last.ay, vz = last.vz + last.sigma * last.az;
                double n = Math.Sqrt(vx * vx + vy * vy + vz * vz);
                double along = (vx * qx + vy * qy + vz * qz) / n;
                qx = (qx - vx / n * along) / n;
                qy = (qy - vy / n * along) / n;
                qz = (qz - vz / n * along) / n;
            }
            offset = new Vector3f((Float)px, (Float)py, (Float)pz);
            turn = new Vector3f((Float)qx, (Float)qy, (Float)qz);
        }

        // The normal of the face of a box that a point is on (the nearest)
        private static void FaceNormal(Bounds3f box, double x, double y, double z, out double nx, out double ny, out double nz)
        {
            double fx = Math.Min(Math.Abs(x - box.pMin.x), Math.Abs(x - box.pMax.x)), fy = Math.Min(Math.Abs(y - box.pMin.y), Math.Abs(y - box.pMax.y));
            double fz = Math.Min(Math.Abs(z - box.pMin.z), Math.Abs(z - box.pMax.z));
            nx = fx <= fy && fx <= fz ? 1 : 0;
            ny = nx == 0 && fy <= fz ? 1 : 0;
            nz = nx == 0 && ny == 0 ? 1 : 0;
        }
    }

    // CurvedMajorantIterator Definition
    public sealed unsafe class CurvedMajorantIterator : RayMajorantIterator
    {
        public CurvedMajorantIterator(CurvedRayMedium medium, double start, Float tMax, SampledWavelengths lambda, ScratchBuffer buf)
        {
            this.medium = medium;
            this.start = start;
            this.tMax = tMax;
            this.lambda = lambda;
            this.buf = buf;
            piece = 0;
        }

        public override pstd.optional<RayMajorantSegment> Next()
        {
            while (true)
            {
                if (straight != null)
                {
                    // The segments of the held medium along a straight piece: its distances over the index are sigma
                    pstd.optional<RayMajorantSegment> seg = straight.Next();
                    if (seg)
                    {
                        RayMajorantSegment s = seg.value();
                        s.tMin = (Float)(offset + s.tMin / index);
                        s.tMax = (Float)(offset + s.tMax / index);
                        s.sigma_maj = s.sigma_maj * (Float)index;
                        if (!(s.tMin < tMax))
                        {
                            return default;
                        }
                        s.tMax = std.min(s.tMax, tMax);
                        return s;
                    }
                    straight = null;
                }
                if (piece >= medium.pieces.Count)
                {
                    return default;
                }
                RefractivePiece p = medium.pieces[piece];
                double from = medium.starts[piece], to = from + p.sigma;
                ++piece;
                if (!(to > start))
                {
                    continue;
                }
                if (!(from - start < tMax))
                {
                    return default;
                }
                double first = Math.Max(from, start);
                if (p.ax == 0 && p.ay == 0 && p.az == 0)
                {
                    index = Math.Sqrt(p.vx * p.vx + p.vy * p.vy + p.vz * p.vz);
                    Vector3f d = new Vector3f((Float)(p.vx / index), (Float)(p.vy / index), (Float)(p.vz / index));
                    double length = Math.Min(to, start + (double)tMax) - first;
                    Float distance = double.IsInfinity(length) ? Infinity : (Float)(length * index);
                    straight = medium.inner.SampleRay(new Ray(p.Point(first - from), d, medium.time), distance, lambda, buf);
                    offset = first - start;
                    continue;
                }
                SampledSpectrum bound;
                Float control, average;
                medium.inner.MajorantOver(Box(p), lambda, out bound, out control, out average);
                RayMajorantSegment segment = new RayMajorantSegment();
                segment.tMin = (Float)(first - start);
                segment.tMax = std.min((Float)(to - start), tMax);
                segment.sigma_maj = bound * (Float)p.largestIndex;
                Float ratio = (Float)(p.smallestIndex / p.largestIndex);
                segment.controlFraction = control * ratio;
                segment.averageFraction = std.max(average * ratio, segment.controlFraction);
                return segment;
            }
        }

        // The box of a piece: its ends and, along each axis, the point where it turns
        private static Bounds3f Box(RefractivePiece p)
        {
            Bounds3f box = new Bounds3f(p.Point(0), p.Point(p.sigma));
            if (p.ax != 0)
            {
                double turn = -p.vx / p.ax;
                if (turn > 0 && turn < p.sigma)
                {
                    box = Union(box, p.Point(turn));
                }
            }
            if (p.ay != 0)
            {
                double turn = -p.vy / p.ay;
                if (turn > 0 && turn < p.sigma)
                {
                    box = Union(box, p.Point(turn));
                }
            }
            if (p.az != 0)
            {
                double turn = -p.vz / p.az;
                if (turn > 0 && turn < p.sigma)
                {
                    box = Union(box, p.Point(turn));
                }
            }
            return box;
        }

        public override string ToString()
        {
            return StringPrintf("[ CurvedMajorantIterator piece: %d ]", piece);
        }

        private CurvedRayMedium medium;
        private double start;
        private Float tMax;
        private SampledWavelengths lambda;
        private ScratchBuffer buf;
        private int piece;
        private RayMajorantIterator straight;
        private double offset, index;
    }

    // CurvedRayMedium Definition: a curved path as a straight ray. The ray is the x axis from the origin; the distance
    // along it is sigma.
    public sealed unsafe class CurvedRayMedium : Medium, IMajorantMedium<RayMajorantIterator>
    {
        public CurvedRayMedium(Medium inner, CurvedPath path, Float time, VolumeIntegrator settings)
        {
            this.inner = inner;
            this.time = time;
            pieces = path.pieces;
            starts = new double[pieces.Count];
            double sum = 0;
            for (int i = 0; i < pieces.Count; ++i)
            {
                starts[i] = sum;
                sum += pieces[i].sigma;
            }
            indexAtStart = path.indexAtStart;
            volumeIntegrator = settings;
        }

        public static Ray Axis(Float time)
        {
            return new Ray(new Point3f(0, 0, 0), new Vector3f(1, 0, 0), time);
        }

        // The point of the curve for a sigma, with the optical velocity there (whose length is the index)
        public Point3f At(double sigma, out Vector3f velocity)
        {
            int low = 0, high = pieces.Count - 1;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (starts[middle] <= sigma)
                {
                    low = middle;
                }
                else
                {
                    high = middle - 1;
                }
            }
            RefractivePiece p = pieces[low];
            double s = Math.Max(sigma - starts[low], 0);
            if (!double.IsInfinity(p.sigma))
            {
                s = Math.Min(s, p.sigma);
            }
            velocity = new Vector3f((Float)(p.vx + s * p.ax), (Float)(p.vy + s * p.ay), (Float)(p.vz + s * p.az));
            return p.Point(s);
        }

        // The directions of the path at its start and at a sigma, each with an axis across it: the second axis is the first
        // carried along the curve. Each piece is a curve in a plane, along which a vector is carried by the turn that takes
        // the direction at its start to the direction at its end.
        public void Carry(double sigma, out Vector3f d0, out Vector3f e0, out Vector3f d1, out Vector3f e1)
        {
            RefractivePiece first = pieces[0];
            double n0 = Math.Sqrt(first.vx * first.vx + first.vy * first.vy + first.vz * first.vz);
            d0 = new Vector3f((Float)(first.vx / n0), (Float)(first.vy / n0), (Float)(first.vz / n0));
            Vector3f across, other;
            CoordinateSystem(d0, &across, &other);
            e0 = across;
            double ex = e0.x, ey = e0.y, ez = e0.z;
            double tx = first.vx / n0, ty = first.vy / n0, tz = first.vz / n0;
            for (int i = 0; i < pieces.Count && starts[i] < sigma; ++i)
            {
                RefractivePiece p = pieces[i];
                if (p.ax == 0 && p.ay == 0 && p.az == 0)
                {
                    continue;
                }
                double s = Math.Min(p.sigma, sigma - starts[i]);
                double ax = p.vx, ay = p.vy, az = p.vz, la = Math.Sqrt(ax * ax + ay * ay + az * az);
                double bx = p.vx + s * p.ax, by = p.vy + s * p.ay, bz = p.vz + s * p.az, lb = Math.Sqrt(bx * bx + by * by + bz * bz);
                ax /= la;
                ay /= la;
                az /= la;
                bx /= lb;
                by /= lb;
                bz /= lb;
                // The turn about a x b that takes a to b, applied to the axis
                double kx = ay * bz - az * by, ky = az * bx - ax * bz, kz = ax * by - ay * bx;
                double sine = Math.Sqrt(kx * kx + ky * ky + kz * kz), cosine = ax * bx + ay * by + az * bz;
                if (sine > 0)
                {
                    kx /= sine;
                    ky /= sine;
                    kz /= sine;
                    double along = kx * ex + ky * ey + kz * ez;
                    double cx = ky * ez - kz * ey, cy = kz * ex - kx * ez, cz = kx * ey - ky * ex;
                    ex = ex * cosine + cx * sine + kx * along * (1 - cosine);
                    ey = ey * cosine + cy * sine + ky * along * (1 - cosine);
                    ez = ez * cosine + cz * sine + kz * along * (1 - cosine);
                }
                tx = bx;
                ty = by;
                tz = bz;
            }
            d1 = new Vector3f((Float)tx, (Float)ty, (Float)tz);
            // (across the direction to the last digit)
            double off = ex * tx + ey * ty + ez * tz;
            ex -= off * tx;
            ey -= off * ty;
            ez -= off * tz;
            double length = Math.Sqrt(ex * ex + ey * ey + ez * ez);
            e1 = new Vector3f((Float)(ex / length), (Float)(ey / length), (Float)(ez / length));
        }

        // Where a point of the scene is for the straight ray that stands for the curve: beside the sigma of the point of
        // the curve that is nearest to it, at that distance over the index there. (Any rule would do for the density that
        // is drawn about it, as long as it is one rule: the weights of the strategies are all taken with it.)
        public Point3f Pivot(Point3f world)
        {
            double best = double.PositiveInfinity, bestSigma = 0, bestIndex = 1;
            for (int i = 0; i < pieces.Count; ++i)
            {
                RefractivePiece p = pieces[i];
                double end = double.IsInfinity(p.sigma) ? 1e30 : p.sigma;
                // The sigma of the piece where the path is nearest: from the foot on the tangent at the start, by Newton's
                // method on (x(s) - y) . v(s)
                double dx = world.x - p.x, dy = world.y - p.y, dz = world.z - p.z;
                double vv = p.vx * p.vx + p.vy * p.vy + p.vz * p.vz;
                double s = Math.Min(Math.Max((dx * p.vx + dy * p.vy + dz * p.vz) / vv, 0), end);
                for (int step = 0; step < 3; ++step)
                {
                    double rx = p.x + s * (p.vx + 0.5 * s * p.ax) - world.x, ry = p.y + s * (p.vy + 0.5 * s * p.ay) - world.y;
                    double rz = p.z + s * (p.vz + 0.5 * s * p.az) - world.z;
                    double wx = p.vx + s * p.ax, wy = p.vy + s * p.ay, wz = p.vz + s * p.az;
                    double f = rx * wx + ry * wy + rz * wz, df = wx * wx + wy * wy + wz * wz + rx * p.ax + ry * p.ay + rz * p.az;
                    if (!(df > 0))
                    {
                        break;
                    }
                    s = Math.Min(Math.Max(s - f / df, 0), end);
                }
                double qx = p.x + s * (p.vx + 0.5 * s * p.ax) - world.x, qy = p.y + s * (p.vy + 0.5 * s * p.ay) - world.y;
                double qz = p.z + s * (p.vz + 0.5 * s * p.az) - world.z;
                double squared = qx * qx + qy * qy + qz * qz;
                if (squared < best)
                {
                    double ux = p.vx + s * p.ax, uy = p.vy + s * p.ay, uz = p.vz + s * p.az;
                    best = squared;
                    bestSigma = starts[i] + s;
                    bestIndex = Math.Sqrt(ux * ux + uy * uy + uz * uz);
                }
            }
            return new Point3f((Float)bestSigma, (Float)(Math.Sqrt(best) / bestIndex), 0);
        }

        public override string ToString()
        {
            return StringPrintf("[ CurvedRayMedium inner: %s ]", inner);
        }

        public override bool IsEmissive() { return inner.IsEmissive(); }

        public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda)
        {
            return SamplePoint(p, lambda, time);
        }

        public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda, Float t)
        {
            Vector3f velocity;
            Point3f world = At(p.x, out velocity);
            Float index = Length(velocity);
            MediumProperties mp = inner.SamplePoint(world, lambda, t);
            mp.sigma_a = mp.sigma_a * index;
            mp.sigma_s = mp.sigma_s * index;
            // (what is emitted at the index n is seen from the start of the path as (n_start / n)^2 of it)
            Float seen = (Float)(indexAtStart / index);
            mp.Le = mp.Le * (seen * seen);
            return mp;
        }

        public RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda)
        {
            return SampleRay(ray, tMax, lambda, new ScratchBuffer());
        }

        public override RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda, ScratchBuffer buf)
        {
            // (the rays that the volumes make are along the axis; their length may not be one)
            Float scale = Length(ray.d);
            return new CurvedMajorantIterator(this, ray.o.x, tMax * scale, lambda, buf);
        }

        public Medium inner;
        public Float time;
        public List<RefractivePiece> pieces;
        public double[] starts;
        public double indexAtStart;
    }

    // RefractiveVolume Definition
    public sealed unsafe class RefractiveVolume : Volume
    {
        public RefractiveVolume(VolumeContext ctx, RefractiveMedium medium) : base(ctx, medium)
        {
            refractive = medium;
        }

        // The services of the renderer for the straight ray that stands for a curved path: the surface at the end of the
        // path is at the distance of its sigma; the random numbers are the renderer's. (The walk of a multiple scattering
        // volume by the densities of the medium asks for nothing else.)
        private sealed class AlongCurve : VolumeServices
        {
            public VolumeServices renderer;
            public CurvedPath path;

            public override bool GetNearestHit(Ray ray, out Point3f P, out Float distance, out VolumeGeometry g)
            {
                g = path.surface;
                if (!path.hit)
                {
                    P = ray.o;
                    distance = Infinity;
                    return false;
                }
                distance = (Float)(path.sigma - ray.o.x);
                P = ray.Apply(distance);
                return true;
            }

            public override Float GenerateRandomNumber() { return renderer.GenerateRandomNumber(); }

            public override bool GenerateLightSample(VolumeContext ctx, MediumInteraction point, PhaseFunction phase, out Vector3f wi, out SampledSpectrum L,
                                                     out SampledSpectrum beamTransmittance, out SampledSpectrum beamR)
            {
                wi = default;
                L = default;
                beamTransmittance = default;
                beamR = new SampledSpectrum(1f);
                return false;
            }

            public override bool GenerateLightSample(VolumeContext ctx, MediumInteraction point, out Vector3f wi, out SampledSpectrum L)
            {
                wi = default;
                L = default;
                return false;
            }

            // The light that a point of the curve is sampled about: the renderer's, with its point where the curve puts it
            public override bool ChooseLight(VolumeContext ctx, out VolumeLight light)
            {
                if (!renderer.ChooseLight(ctx, out light))
                {
                    return false;
                }
                if (light.hasPoint)
                {
                    light.point = curve.Pivot(light.point);
                }
                return true;
            }

            // The light at a point of the curve: the renderer's, at the point of the scene, with the direction to the light
            // as the straight ray sees it (at the angle to the axis that it is at to the path there), and as much of it as
            // is seen from the start of the path
            public override bool EvaluateLight(VolumeContext ctx, VolumeLight light, VolumePlace place, MediumInteraction point, out Vector3f wi, out SampledSpectrum L,
                                               out SampledSpectrum beamTransmittance, out SampledSpectrum beamR)
            {
                Vector3f velocity;
                Point3f world = curve.At(point.p().x, out velocity);
                Float index = Length(velocity);
                Vector3f tangent = velocity / index;
                Point3f saved = ctx.P;
                ctx.SetP(world);
                MediumInteraction there = new MediumInteraction(world, -tangent, point.time, medium, point.phase);
                Vector3f toLight;
                bool lit = renderer.EvaluateLight(ctx, light, place, there, out toLight, out L, out beamTransmittance, out beamR);
                ctx.SetP(saved);
                wi = default;
                if (!lit)
                {
                    return false;
                }
                Vector3f b1, b2;
                CoordinateSystem(tangent, &b1, &b2);
                wi = new Vector3f(Dot(toLight, tangent), Dot(toLight, b1), Dot(toLight, b2));
                L *= Seen(path.indexAtStart, index);
                return true;
            }

            public CurvedRayMedium curve;
            public Medium medium;
        }

        // The settings of the medium for a walk along a curve: its tracker and its estimator, scattering any number of
        // times, the distances by the densities of the medium (the strategies that are built on straight connections to
        // a light are not for a curved path yet)
        private VolumeIntegrator AlongCurveSettings()
        {
            VolumeIntegrator from = refractive.inner.volumeIntegrator;
            VolumeIntegrator settings = new VolumeIntegrator(from.tracker, from.transmittance);
            settings.scattering = from.scattering == VolumeScattering.None || from.scattering == VolumeScattering.Absorption ? from.scattering : VolumeScattering.Multiple;
            settings.stepSize = from.stepSize;
            settings.contrast = from.contrast;
            settings.spectralTracking = from.spectralTracking;
            settings.spectralMaximum = from.spectralMaximum;
            settings.spectralAbsorption = from.spectralAbsorption;
            settings.emissionResidual = from.emissionResidual;
            // (the equiangular point: the straight ray that stands for the curve draws it about where the curve puts the
            // point of the light)
            settings.distanceSampling = from.distanceSampling;
            return settings;
        }

        // The path from the start of a ray along its direction: through the field piece by piece, with the surfaces along
        // the way, to the first surface or out of the scene
        private CurvedPath Trace(VolumeServices rs, Ray wi)
        {
            return Trace(rs, wi, new CurvedPath());
        }

        // (a path of this thread that is used again by the walks that want the geometry alone, which keep nothing of it)
        [ThreadStatic]
        private static CurvedPath scratch;

        private static CurvedPath Scratch()
        {
            if (scratch == null)
            {
                scratch = new CurvedPath();
            }
            scratch.Reset();
            return scratch;
        }

        private CurvedPath Trace(VolumeServices rs, Ray wi, CurvedPath path)
        {
            TetrahedralIndexField field = refractive.field;
            Vector3f d = Normalize(wi.d);
            Point3f o = wi.o;
            Bounds3f box = field.Bounds();
            // Up to the field, where the ray starts outside it: a straight piece, with the index of the field where it enters
            Float t0 = 0, t1 = 0;
            bool meets = box.IntersectP(o, d, Infinity, &t0, &t1);
            Point3f P;
            Float distance;
            VolumeGeometry g;
            if (!meets || t0 > 0)
            {
                bool hit = rs.GetNearestHit(new Ray(o, d, wi.time), out P, out distance, out g);
                if (!meets || (hit && distance <= t0))
                {
                    // (the ray never reaches the field: it is straight)
                    path.hit = hit;
                    path.surface = g;
                    path.P = P;
                    path.arrival = new Ray(o, d, wi.time);
                    path.indexAtStart = path.indexAtEnd = 1;
                    return path;
                }
                if (PlaneOnLine(o, d, t0, out distance))
                {
                    // (the plane is before the field: the ray is straight up to it)
                    path.atPlane = true;
                    path.P = o + d * distance;
                    path.arrival = new Ray(o, d, wi.time);
                    path.indexAtStart = path.indexAtEnd = 1;
                    return path;
                }
                Point3f entry = o + d * t0;
                double index = field.Index(entry);
                RefractivePiece lead = new RefractivePiece();
                lead.x = o.x;
                lead.y = o.y;
                lead.z = o.z;
                lead.vx = index * d.x;
                lead.vy = index * d.y;
                lead.vz = index * d.z;
                lead.sigma = t0 / index;
                lead.largestIndex = lead.smallestIndex = index;
                path.lead = true;
                path.pieces.Add(lead);
                path.sigma += lead.sigma;
                o = entry;
            }
            RefractiveRay ray = new RefractiveRay();
            if (!ray.Begin(field, o, d))
            {
                // (on the far face of the box, going out: straight)
                bool hit = rs.GetNearestHit(new Ray(wi.o, d, wi.time), out P, out distance, out g);
                path.pieces.Clear();
                path.lead = false;
                path.sigma = 0;
                path.hit = hit;
                path.surface = g;
                path.P = P;
                path.arrival = new Ray(wi.o, d, wi.time);
                path.indexAtStart = path.indexAtEnd = 1;
                return path;
            }
            path.indexAtStart = path.pieces.Count > 0 ? path.pieces[0].largestIndex : Math.Sqrt(ray.vx * ray.vx + ray.vy * ray.vy + ray.vz * ray.vz);
            RefractivePiece piece;
            while (ray.Next(out piece))
            {
                bool curved = piece.ax != 0 || piece.ay != 0 || piece.az != 0;
                path.bent |= curved;
                double atPlane;
                bool plane = PlaneOnPiece(piece, out atPlane);
                if (plane)
                {
                    piece.sigma = atPlane;
                }
                if (refractive.surfacesInside && piece.sigma > 0 && Stopped(rs, wi.time, ref piece, curved, path))
                {
                    path.pieces.Add(piece);
                    path.sigma += piece.sigma;
                    return path;
                }
                if (plane)
                {
                    double vx = piece.vx + atPlane * piece.ax, vy = piece.vy + atPlane * piece.ay, vz = piece.vz + atPlane * piece.az;
                    double n = Math.Sqrt(vx * vx + vy * vy + vz * vz);
                    path.pieces.Add(piece);
                    path.sigma += piece.sigma;
                    path.atPlane = true;
                    path.P = piece.Point(atPlane);
                    path.arrival = new Ray(path.P, new Vector3f((Float)(vx / n), (Float)(vy / n), (Float)(vz / n)), wi.time);
                    path.indexAtEnd = n;
                    return path;
                }
                path.pieces.Add(piece);
                path.sigma += piece.sigma;
                if (path.pieces.Count > MostPieces)
                {
                    // (a path that does not come out: it ends here, unseen)
                    path.hit = false;
                    path.arrival = new Ray(new Point3f((Float)ray.x, (Float)ray.y, (Float)ray.z), d, wi.time);
                    path.indexAtEnd = path.indexAtStart;
                    path.sigma = double.PositiveInfinity;
                    path.lost = true;
                    return path;
                }
            }
            // Beyond the field: straight, from a little before the point where the path left it (a surface that bounds the
            // medium may be the face of the box itself)
            double exitIndex = Math.Sqrt(ray.vx * ray.vx + ray.vy * ray.vy + ray.vz * ray.vz);
            Vector3f e = new Vector3f((Float)(ray.vx / exitIndex), (Float)(ray.vy / exitIndex), (Float)(ray.vz / exitIndex));
            Float back = 0.01f * (Float)Math.Min(field.sizeX, Math.Min(field.sizeY, field.sizeZ));
            Point3f exit = new Point3f((Float)ray.x, (Float)ray.y, (Float)ray.z);
            Ray beyond = new Ray(exit - back * e, e, wi.time);
            path.hit = rs.GetNearestHit(beyond, out P, out distance, out g);
            path.surface = g;
            path.P = P;
            path.arrival = beyond;
            path.indexAtEnd = exitIndex;
            Float toPlane;
            if (PlaneOnLine(beyond.o, e, path.hit ? distance : Infinity, out toPlane))
            {
                path.hit = false;
                path.surface = null;
                path.atPlane = true;
                path.P = beyond.Apply(toPlane);
                distance = toPlane;
            }
            RefractivePiece last = new RefractivePiece();
            last.x = exit.x;
            last.y = exit.y;
            last.z = exit.z;
            last.vx = exitIndex * e.x;
            last.vy = exitIndex * e.y;
            last.vz = exitIndex * e.z;
            last.sigma = path.hit || path.atPlane ? Math.Max(distance - back, 0) / exitIndex : double.PositiveInfinity;
            last.largestIndex = last.smallestIndex = exitIndex;
            path.beyond = true;
            path.pieces.Add(last);
            path.sigma += last.sigma;
            return path;
        }

        // Where the plane that stops the ray (VolumeContext.stopAtPlane) meets a straight line within a distance, coming
        // from behind it
        private bool PlaneOnLine(Point3f o, Vector3f d, Float within, out Float distance)
        {
            distance = 0;
            if (!m_ctx.stopAtPlane)
            {
                return false;
            }
            Float rate = Dot(d, m_ctx.stopNormal), behind = Dot(m_ctx.stopPoint - o, m_ctx.stopNormal);
            if (!(rate > 0) || !(behind >= 0))
            {
                return false;
            }
            distance = behind / rate;
            return distance <= within;
        }

        // The same for a piece of the path: the first sigma where (x(sigma) - point) . normal rises to zero
        private bool PlaneOnPiece(RefractivePiece piece, out double sigma)
        {
            sigma = 0;
            if (!m_ctx.stopAtPlane)
            {
                return false;
            }
            double nx = m_ctx.stopNormal.x, ny = m_ctx.stopNormal.y, nz = m_ctx.stopNormal.z;
            double s = (m_ctx.stopPoint.x - piece.x) * nx + (m_ctx.stopPoint.y - piece.y) * ny + (m_ctx.stopPoint.z - piece.z) * nz;
            double p = piece.vx * nx + piece.vy * ny + piece.vz * nz, q = piece.ax * nx + piece.ay * ny + piece.az * nz;
            if (s < 0)
            {
                return false;
            }
            // s - p sigma - 1/2 q sigma^2 falls to zero
            double discriminant = p * p + 2 * q * s;
            if (discriminant < 0)
            {
                return false;
            }
            double denominator = Math.Sqrt(discriminant) + p;
            if (!(denominator > 0))
            {
                return false;
            }
            sigma = 2 * s / denominator;
            return sigma <= piece.sigma;
        }

        // Whether a surface stops a piece of the path: the piece against the scene as a chain of chords, as many as keep
        // the chords within a ten-thousandth of their length of the curve. The piece is then cut at the surface.
        private bool Stopped(VolumeServices rs, Float time, ref RefractivePiece piece, bool curved, CurvedPath path)
        {
            int chords = 1;
            if (curved)
            {
                Point3f a = piece.Point(0), b = piece.Point(piece.sigma), m = piece.Point(0.5 * piece.sigma);
                Float chord = Distance(a, b);
                Float sagitta = Distance(m, a + 0.5f * (b - a));
                if (chord > 0 && sagitta > 1e-4f * chord)
                {
                    chords = Math.Min((int)Math.Ceiling(Math.Sqrt(sagitta / (1e-4f * chord))), 16);
                }
            }
            for (int c = 0; c < chords; ++c)
            {
                double from = piece.sigma * c / chords, to = piece.sigma * (c + 1) / chords;
                Point3f a = piece.Point(from), b = piece.Point(to);
                Float length = Distance(a, b);
                if (!(length > 0))
                {
                    continue;
                }
                Ray chord = new Ray(a, (b - a) / length, time);
                Point3f P;
                Float distance;
                VolumeGeometry g;
                if (rs.GetNearestHit(chord, out P, out distance, out g) && distance <= length * (1 + 1e-4f))
                {
                    double at = from + (to - from) * Math.Min(distance / length, 1);
                    double vx = piece.vx + at * piece.ax, vy = piece.vy + at * piece.ay, vz = piece.vz + at * piece.az;
                    piece.sigma = at;
                    path.hit = true;
                    path.surface = g;
                    path.P = P;
                    path.arrival = chord;
                    path.indexAtEnd = Math.Sqrt(vx * vx + vy * vy + vz * vz);
                    return true;
                }
            }
            return false;
        }

        // The weight of what is at the far end of the path, from its start: (n_start / n_end)^2
        private static Float Seen(double near, double far)
        {
            return (Float)((near / far) * (near / far));
        }

        public override bool Integrate(VolumeServices rs, Ray wi, out SampledSpectrum L, out SampledSpectrum transmittance, out SampledSpectrum weight,
                                       out Point3f P, out Ray wo, out VolumeGeometry g)
        {
            CurvedPath path = Trace(rs, wi);
            Medium saved = m_ctx.medium;
            if (!path.bent)
            {
                // A ray that the field leaves straight: the volume of the held medium, as it is
                m_ctx.medium = refractive.inner;
                bool straight = Create(m_ctx, refractive.inner).Integrate(rs, wi, out L, out transmittance, out weight, out P, out wo, out g);
                m_ctx.medium = saved;
                return straight;
            }
            if (path.lost)
            {
                L = new SampledSpectrum(0f);
                transmittance = new SampledSpectrum(1f);
                weight = new SampledSpectrum(0f);
                P = wi.o;
                wo = wi;
                g = null;
                return false;
            }
            CurvedRayMedium curve = new CurvedRayMedium(refractive.inner, path, wi.time, AlongCurveSettings());
            AlongCurve along = new AlongCurve();
            along.renderer = rs;
            along.path = path;
            along.curve = curve;
            along.medium = medium;
            along.rng = rs.rng;
            VolumeJoint joint = m_ctx.joint;
            m_ctx.joint = null;
            m_ctx.medium = curve;
            Point3f onAxis;
            Ray leaves;
            bool hit = Create(m_ctx, curve).Integrate(along, CurvedRayMedium.Axis(wi.time), out L, out transmittance, out weight, out onAxis, out leaves, out g);
            rs.rng = along.rng;
            m_ctx.joint = joint;
            m_ctx.medium = saved;
            P = path.P;
            wo = path.arrival;
            if (hit && g != null && !g.isSurface)
            {
                // A collision on the curve: where it is, and the direction of the path there
                Vector3f velocity;
                Point3f point = curve.At(onAxis.x, out velocity);
                Float index = Length(velocity);
                Vector3f tangent = velocity / index;
                weight *= Seen(path.indexAtStart, index);
                m_ctx.bent = true;
                m_ctx.arrival = new Ray(point, tangent, wi.time);
                curve.Carry(onAxis.x, out m_ctx.startDirection, out m_ctx.startAxis, out m_ctx.arrivalDirection, out m_ctx.arrivalAxis);
                PhaseFunction phase = g.phase;
                VolumeDirectWeight direct = g.direct;
                if (direct != null)
                {
                    direct.curve = curve;
                    direct.worldOrigin = wi.o;
                }
                g = new VolumeGeometry();
                g.isSurface = false;
                g.phase = phase;
                g.direct = direct;
                g.scatter = new MediumInteraction(point, -tangent, wi.time, medium, phase);
                P = point;
                wo = new Ray(point, tangent, wi.time);
                return true;
            }
            // The surface at the end of the path, or none: the path tracer goes on with the ray as it arrives
            weight *= Seen(path.indexAtStart, path.indexAtEnd);
            m_ctx.bent = true;
            m_ctx.arrival = path.arrival;
            curve.Carry(double.PositiveInfinity, out m_ctx.startDirection, out m_ctx.startAxis, out m_ctx.arrivalDirection, out m_ctx.arrivalAxis);
            return hit;
        }

        public override SampledSpectrum Transmit(VolumeServices rs, Ray entering, Float tMax, out bool bent, out bool hit, out Point3f P, out VolumeGeometry g,
                                                 out Ray arrival)
        {
            CurvedPath path = m_ctx.geometryOnly ? Trace(rs, entering, Scratch()) : Trace(rs, entering);
            Medium saved = m_ctx.medium;
            bent = path.bent;
            hit = path.hit;
            P = path.P;
            g = path.surface;
            arrival = path.arrival;
            m_ctx.atPlane = false;
            if (!path.bent)
            {
                if (m_ctx.geometryOnly)
                {
                    return new SampledSpectrum(1f);
                }
                m_ctx.medium = refractive.inner;
                SampledSpectrum straight = Create(m_ctx, refractive.inner).Transmittance(rs, entering.o, entering.Apply(tMax));
                m_ctx.medium = saved;
                return straight;
            }
            if (path.lost)
            {
                return new SampledSpectrum(0f);
            }
            m_ctx.atPlane = path.atPlane;
            if (m_ctx.differentials)
            {
                Bounds3f box = refractive.field.Bounds();
                path.Carry(box, ref m_ctx.offset1, ref m_ctx.turn1);
                path.Carry(box, ref m_ctx.offset2, ref m_ctx.turn2);
            }
            if (m_ctx.geometryOnly)
            {
                return new SampledSpectrum(1f);
            }
            CurvedRayMedium curve = new CurvedRayMedium(refractive.inner, path, entering.time, AlongCurveSettings());
            AlongCurve along = new AlongCurve();
            along.renderer = rs;
            along.path = path;
            along.rng = rs.rng;
            m_ctx.medium = curve;
            Float end = path.hit || path.atPlane ? (Float)path.sigma : Unbounded;
            SampledSpectrum T = Create(m_ctx, curve).Transmittance(along, new Point3f(0, 0, 0), new Point3f(end, 0, 0));
            rs.rng = along.rng;
            m_ctx.medium = saved;
            return T * Seen(path.indexAtStart, path.indexAtEnd);
        }

        // (between two points along a straight line: for the callers that know no better; what the held medium gives)
        public override SampledSpectrum Transmittance(VolumeServices rs, Point3f P0, Point3f P1)
        {
            Medium saved = m_ctx.medium;
            m_ctx.medium = refractive.inner;
            SampledSpectrum T = Create(m_ctx, refractive.inner).Transmittance(rs, P0, P1);
            m_ctx.medium = saved;
            return T;
        }

        public override SampledSpectrum Emission(VolumeServices rs, Point3f P0, Point3f P1)
        {
            Medium saved = m_ctx.medium;
            m_ctx.medium = refractive.inner;
            SampledSpectrum emitted = Create(m_ctx, refractive.inner).Emission(rs, P0, P1);
            m_ctx.medium = saved;
            return emitted;
        }

        // The most pieces of a path (one that goes round and round in a lens that holds it)
        private const int MostPieces = 100000;
        // The length of a path that leaves the scene, in sigma
        private const Float Unbounded = 1e18f;

        private RefractiveMedium refractive;
    }
}
