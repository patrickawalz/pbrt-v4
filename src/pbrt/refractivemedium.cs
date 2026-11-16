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
            // (the light that samples the emission of the held medium is this medium's: the directions that it draws are
            // followed as the field bends them)
            emissionLight = inner.emissionLight;
        }

        public override bool BendsRays() { return true; }

        // How far from the path the chords that it is tested against the surfaces by may be, in scene units ("float
        // surfacetolerance"); without it they are within a ten-thousandth of their length, which over the kilometres of a
        // piece of an atmosphere is metres: a ray that passes low over the sea would meet it or not by chance.
        public Float surfaceTolerance = Infinity;
        // "bool bentjoint" (true), "bool bentequiangular" (true): the two scatterings that "doublescattering" draws jointly
        // and the equiangular point of "distancesampling" are drawn along bent rays too (JointShareBent; the walk along
        // the curve). Off, they are drawn on no ray of the medium (joint) or on the rays that the field leaves straight
        // (equiangular).
        public bool bentJoint = true;
        public bool bentEquiangular = true;

        // Dispersion: for a wavelength (nm), how many times the n^2 - 1 of the field the medium has; null for none. A
        // scene with such a medium is rendered one wavelength to a path (ProductionPath, by `anyDispersion`).
        public Func<Float, double> dispersion;
        public static bool anyDispersion;

        // The refractivity of standard air (15 C, 101325 Pa, dry) at a wavelength in nm: Edlen's formula of 1966
        public static double AirRefractivity(double lambdaNm)
        {
            double s = 1000 / lambdaNm, s2 = s * s;
            return 1e-8 * (8342.54 + 2406147 / (130 - s2) + 15998 / (38.9 - s2));
        }

        // An atmosphere that refracts (scene type "refractiveatmosphere"): the "atmosphere" medium with all of its
        // parameters, held by a refractive medium whose index is that of air of the density that the atmosphere has at each
        // height (the refractivity of a gas is in proportion to its density), in closed form on a lattice that stores
        // nothing. The lattice is a box about the top of the planet (the point above its centre along +y), where the
        // viewer is taken to be:
        //     "float indexextent" (1.5e6)     how far the box reaches to each side, in metres
        //     "float indexcellacross" (1e4)   the size of its cells across, in metres
        //     "float indexcellup" (200)       and up
        //     "float indextop" (60000)        the height that the box reaches to: the air above it bends nothing (it has
        //                                     0.02 % of the refractivity of the air at the ground at 60 km)
        //     "bool dispersion" (true)        the index changes with the wavelength (and a path carries one wavelength)
        public static RefractiveMedium CreateAtmosphere(ParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            AtmosphereMedium inner = AtmosphereMedium.Create(parameters, loc, alloc);
            inner.volumeIntegrator = VolumeIntegrator.Create(parameters, loc);
            double unit = inner.MetersPerUnit(), radius = inner.PlanetRadiusUnits() * unit;
            double top = Math.Min(inner.TopAltitudeMeters(), parameters.GetOneFloat("indextop", 60000f));
            double extent = parameters.GetOneFloat("indexextent", 1.5e6f), across = parameters.GetOneFloat("indexcellacross", 1e4f), up = parameters.GetOneFloat("indexcellup", 200f);
            bool disperses = parameters.GetOneBool("dispersion", true);
            double cx = inner.CenterX(), cy = inner.CenterY(), cz = inner.CenterZ();
            // (the box reaches as far below the top of the planet as the planet falls away at its sides, and a path bends)
            double below = extent * extent / (2 * radius) * 1.3 + 2000;
            Bounds3f box = new Bounds3f(new Point3f((Float)(cx - extent / unit), (Float)(cy + (radius - below) / unit), (Float)(cz - extent / unit)),
                                        new Point3f((Float)(cx + extent / unit), (Float)(cy + (radius + top) / unit), (Float)(cz + extent / unit)));
            AtmosphereProfile profile = inner.Profile();
            double reference = AirRefractivity(550);
            Func<double, double, double, double> index = (double x, double y, double z) =>
            {
                double dx = x - cx, dy = y - cy, dz = z - cz;
                double h = (Math.Sqrt(dx * dx + dy * dy + dz * dz) - radius / unit) * unit;
                // (below the ground the density goes on as it does just above it: the lattice has points there)
                double density = h >= 0 ? profile.RelativeDensity(h) : Math.Exp(-h / 10400);
                return h >= top ? 1 : 1 + reference * density;
            };
            int cellsAcross = Math.Max(1, (int)Math.Ceiling(2 * extent / across)), cellsUp = Math.Max(1, (int)Math.Ceiling((below + top) / up));
            TetrahedralIndexField field = new TetrahedralIndexField(box, cellsAcross, cellsUp, cellsAcross, index, true);
            RefractiveMedium medium = new RefractiveMedium(inner, field, parameters.GetOneBool("surfacesinside", true));
            medium.surfaceTolerance = parameters.GetOneFloat("surfacetolerance", (Float)(0.1 / unit));
            if (disperses)
            {
                medium.dispersion = (Float lambdaNm) => { return AirRefractivity(lambdaNm) / reference; };
                anyDispersion = true;
            }
            return medium;
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
            // (read before the held medium is made, which reports what nobody has read)
            bool bentJoint = parameters.GetOneBool("bentjoint", true), bentEquiangular = parameters.GetOneBool("bentequiangular", true);
            Float surfaceTolerance = parameters.GetOneFloat("surfacetolerance", Infinity);
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
            SampledSpectrum bound;
            Float control, average;
            if (!inner.MajorantOver(box, SampledWavelengths.SampleUniform(0.5f), out bound, out control, out average))
                ErrorExit(loc, $"{innerName}: a refractive medium can hold the media that bound their extinction over a box (Medium.MajorantOver): " +
                               "\"homogeneous\", the grid media, \"cloud\", \"nanovdb\", \"texture\" and \"osl\" (not moving), \"atmosphere\" and aggregates of those.");
            RefractiveMedium made = new RefractiveMedium(inner, field, surfacesInside);
            made.bentJoint = bentJoint;
            made.bentEquiangular = bentEquiangular;
            made.surfaceTolerance = surfaceTolerance;
            return made;
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
    public sealed unsafe class CurvedPath
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
            Carry(box, double.PositiveInfinity, ref offset, ref turn);
        }

        // The area across the path at a sigma per solid angle of directions at its start (the squared distance, for a
        // straight path), with the direction and the index there
        public Float Spread(Bounds3f box, double sigma, out Vector3f direction, out double index)
        {
            RefractivePiece first = pieces[0];
            Vector3f start = Normalize(new Vector3f((Float)first.vx, (Float)first.vy, (Float)first.vz)), a1, a2;
            CoordinateSystem(start, &a1, &a2);
            Vector3f p1 = new Vector3f(0, 0, 0), p2 = new Vector3f(0, 0, 0);
            Carry(box, sigma, ref p1, ref a1);
            Carry(box, sigma, ref p2, ref a2);
            double remaining = sigma;
            direction = start;
            index = 1;
            for (int k = 0; k < pieces.Count; ++k)
            {
                RefractivePiece piece = pieces[k];
                double within = Math.Min(piece.sigma, remaining);
                double vx = piece.vx + within * piece.ax, vy = piece.vy + within * piece.ay, vz = piece.vz + within * piece.az;
                index = Math.Sqrt(vx * vx + vy * vy + vz * vz);
                direction = new Vector3f((Float)(vx / index), (Float)(vy / index), (Float)(vz / index));
                remaining -= within;
                if (!(remaining > 0))
                {
                    break;
                }
            }
            return AbsDot(Cross(p1, p2), direction);
        }

        // (`upTo`: the sigma of the path that the changes are carried to)
        public void Carry(Bounds3f box, double upTo, ref Vector3f offset, ref Vector3f turn)
        {
            double px = offset.x, py = offset.y, pz = offset.z, qx = turn.x, qy = turn.y, qz = turn.z;
            bool inside = false;
            double remaining = upTo;
            int ended = pieces.Count - 1;
            double endedAt = pieces.Count > 0 ? pieces[pieces.Count - 1].sigma : 0;
            for (int k = 0; k < pieces.Count; ++k)
            {
                RefractivePiece piece = pieces[k];
                bool cut = piece.sigma > remaining;
                if (cut)
                {
                    piece.sigma = remaining;
                    ended = k;
                    endedAt = remaining;
                }
                remaining -= piece.sigma;
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
                    if (cut)
                    {
                        break;
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
                if (cut)
                {
                    break;
                }
            }
            if (inside)
            {
                // An end inside the field: to the direction there
                RefractivePiece last = pieces[ended];
                double vx = last.vx + endedAt * last.ax, vy = last.vy + endedAt * last.ay, vz = last.vz + endedAt * last.az;
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

    // JointOnCurve Definition: the collision that a curved ray leaves (VolumeJoint), for the densities of its rivals at the
    // points of the ray: those of the points of the scene, times the volume across the path per solid angle and sigma there
    // over the squared distance (one, for a straight ray).
    public sealed class JointOnCurve
    {
        public VolumeJoint joint;
        public CurvedRayMedium curve;
        public CurvedPath path;
        public Bounds3f box;
        public Point3f origin;

        public Float Stretch(Float sigma)
        {
            Vector3f direction, velocity;
            double index;
            Float spread = path.Spread(box, sigma, out direction, out index);
            Float squared = DistanceSquared(origin, curve.At(sigma, out velocity));
            return squared > 0 ? spread * (Float)index / squared : 1;
        }

        public Float Rivals(Point3f lightPoint, Float sigma)
        {
            Vector3f velocity;
            Point3f b = curve.At(sigma, out velocity);
            return (joint.Ratio(lightPoint, b) + joint.OnceMore(lightPoint, b)) * Stretch(sigma);
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
                medium.inner.MajorantOnPiece(Box(p), p.Point(0), p.Point(0.5 * p.sigma), p.Point(p.sigma), lambda, out bound, out control, out average);
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

            // (the random numbers of the walk along the curve are this object's own while it lasts: the renderer takes
            // them over for what it is asked, and hands them back -- or its own walks, the shadow rays of a light sample
            // among them, would use the numbers that the walk along the curve has just used)
            public override Float GenerateRandomNumber()
            {
                renderer.rng = rng;
                Float u = renderer.GenerateRandomNumber();
                rng = renderer.rng;
                return u;
            }

            // The light sample of a point of the curve (for the volumes that light their points themselves: single
            // scattering): the renderer's for the point of the scene, which follows the way to the light as the field
            // bends it; the direction comes back in the frame of the straight ray that stands for the curve
            public override bool GenerateLightSample(VolumeContext ctx, MediumInteraction point, PhaseFunction phase, out Vector3f wi, out SampledSpectrum L,
                                                     out SampledSpectrum beamTransmittance, out SampledSpectrum beamR)
            {
                Vector3f tangent;
                Float index;
                MediumInteraction there = InScene(ctx, point, out tangent, out index);
                Point3f saved = ctx.P;
                ctx.SetP(there.p());
                renderer.rng = rng;
                Vector3f toLight;
                bool lit = renderer.GenerateLightSample(ctx, there, phase, out toLight, out L, out beamTransmittance, out beamR);
                rng = renderer.rng;
                ctx.SetP(saved);
                wi = lit ? OnAxis(toLight, tangent) : default;
                if (lit)
                {
                    L *= Seen(path.indexAtStart, index);
                }
                return lit;
            }

            public override bool GenerateLightSample(VolumeContext ctx, MediumInteraction point, out Vector3f wi, out SampledSpectrum L)
            {
                Vector3f tangent;
                Float index;
                MediumInteraction there = InScene(ctx, point, out tangent, out index);
                Point3f saved = ctx.P;
                ctx.SetP(there.p());
                renderer.rng = rng;
                Vector3f toLight;
                bool lit = renderer.GenerateLightSample(ctx, there, out toLight, out L);
                rng = renderer.rng;
                ctx.SetP(saved);
                wi = lit ? OnAxis(toLight, tangent) : default;
                if (lit)
                {
                    L *= Seen(path.indexAtStart, index);
                }
                return lit;
            }

            private MediumInteraction InScene(VolumeContext ctx, MediumInteraction point, out Vector3f tangent, out Float index)
            {
                Vector3f velocity;
                Point3f world = curve.At(point.p().x, out velocity);
                index = Length(velocity);
                tangent = velocity / index;
                return new MediumInteraction(world, -tangent, point.time, medium, point.phase);
            }

            private static Vector3f OnAxis(Vector3f direction, Vector3f tangent)
            {
                Vector3f b1, b2;
                CoordinateSystem(tangent, &b1, &b2);
                return new Vector3f(Dot(direction, tangent), Dot(direction, b1), Dot(direction, b2));
            }

            // The light that a point of the curve is sampled about: the renderer's, with its point where the curve puts it
            public override bool ChooseLight(VolumeContext ctx, out VolumeLight light)
            {
                renderer.rng = rng;
                bool chosen = renderer.ChooseLight(ctx, out light);
                rng = renderer.rng;
                if (!chosen)
                {
                    return false;
                }
                if (light.hasPoint)
                {
                    light.worldPoint = light.point;
                    light.onCurve = true;
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
                renderer.rng = rng;
                bool lit = renderer.EvaluateLight(ctx, light, place, there, out toLight, out L, out beamTransmittance, out beamR);
                rng = renderer.rng;
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
            settings.scattering = from.scattering;
            // (a volume that lights its points itself does so by the light sample; the sample of the phase function and
            // of the emission from a point of the curve are not followed along bent rays)
            settings.lightSamples = from.lightSamples;
            settings.stepSize = from.stepSize;
            settings.marchingLookups = from.marchingLookups;
            settings.contrast = from.contrast;
            settings.spectralTracking = from.spectralTracking;
            settings.spectralMaximum = from.spectralMaximum;
            settings.spectralAbsorption = from.spectralAbsorption;
            settings.emissionResidual = from.emissionResidual;
            // (the equiangular point: the straight ray that stands for the curve draws it about where the curve puts the
            // point of the light)
            settings.distanceSampling = refractive.bentEquiangular ? from.distanceSampling : VolumeDistanceSampling.Density;
            // (one more scattering before a light: from the collisions of the curve, by the path tracer, in the scene)
            settings.onceMore = from.onceMore;
            // (the two points drawn jointly: by this volume, in the scene (JointShareBent); the walk keeps the record of
            // its collision for the densities)
            settings.doubleScattering = refractive.bentJoint ? from.doubleScattering : VolumeDoubleScattering.None;
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
            // (the index of the first wavelength of the path, the only one it carries where the medium disperses)
            TetrahedralIndexField.dispersionShift = refractive.dispersion != null ? refractive.dispersion(m_ctx.lambda[0]) - 1 : 0;
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
            // (the first of the pieces that have not been tested against the surfaces yet)
            int untested = path.pieces.Count;
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
                path.pieces.Add(piece);
                path.sigma += piece.sigma;
                if (refractive.surfacesInside && StoppedSoFar(rs, wi.time, path, ref untested, plane))
                {
                    return path;
                }
                if (plane)
                {
                    double vx = piece.vx + atPlane * piece.ax, vy = piece.vy + atPlane * piece.ay, vz = piece.vz + atPlane * piece.az;
                    double n = Math.Sqrt(vx * vx + vy * vy + vz * vz);
                    path.atPlane = true;
                    path.P = piece.Point(atPlane);
                    path.arrival = new Ray(path.P, new Vector3f((Float)(vx / n), (Float)(vy / n), (Float)(vz / n)), wi.time);
                    path.indexAtEnd = n;
                    return path;
                }
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
            if (refractive.surfacesInside && StoppedSoFar(rs, wi.time, path, ref untested, true))
            {
                return path;
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

        // The direction of a piece at a sigma
        private static void DirectionAt(RefractivePiece piece, double sigma, out double dx, out double dy, out double dz)
        {
            dx = piece.vx + sigma * piece.ax;
            dy = piece.vy + sigma * piece.ay;
            dz = piece.vz + sigma * piece.az;
            double n = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            dx /= n;
            dy /= n;
            dz /= n;
        }

        // The pieces of the path from `untested` on against the surfaces of the scene, the last one just added: as long
        // as the path has turned by less than `Together` since the first of them they wait, to be tested as one chord
        // (which is then within a ten-thousandth of its length of the path); a piece that turns by more than that itself
        // is tested alone, by its own chords (Stopped). `now`: test what waits. True where a surface stops the path, which
        // is then cut there.
        private bool StoppedSoFar(VolumeServices rs, Float time, CurvedPath path, ref int untested, bool now)
        {
            const double Together = 4e-4;
            int last = path.pieces.Count - 1;
            if (untested > last)
            {
                return false;
            }
            double ax, ay, az, bx, by, bz;
            DirectionAt(path.pieces[untested], 0, out ax, out ay, out az);
            DirectionAt(path.pieces[last], path.pieces[last].sigma, out bx, out by, out bz);
            double cx = ay * bz - az * by, cy = az * bx - ax * bz, cz = ax * by - ay * bx;
            // (a path that has turned by an angle over a length is within a quarter of their product of its chord)
            bool far = false;
            if (!IsInf(refractive.surfaceTolerance))
            {
                RefractivePiece first = path.pieces[untested], end = path.pieces[last];
                far = 0.25 * Math.Sqrt(cx * cx + cy * cy + cz * cz) * Distance(first.Point(0), end.Point(end.sigma)) > refractive.surfaceTolerance;
            }
            if (Math.Sqrt(cx * cx + cy * cy + cz * cz) > Together || far)
            {
                if (last > untested)
                {
                    if (StoppedOnChord(rs, time, path, untested, last - 1))
                    {
                        return true;
                    }
                    untested = last;
                    DirectionAt(path.pieces[last], 0, out ax, out ay, out az);
                    cx = ay * bz - az * by;
                    cy = az * bx - ax * bz;
                    cz = ax * by - ay * bx;
                }
                if (!IsInf(refractive.surfaceTolerance))
                {
                    RefractivePiece only = path.pieces[last];
                    far = 0.25 * Math.Sqrt(cx * cx + cy * cy + cz * cz) * Distance(only.Point(0), only.Point(only.sigma)) > refractive.surfaceTolerance;
                }
                if (Math.Sqrt(cx * cx + cy * cy + cz * cz) > Together || far)
                {
                    RefractivePiece alone = path.pieces[last];
                    double whole = alone.sigma;
                    untested = last + 1;
                    if (alone.sigma > 0 && Stopped(rs, time, ref alone, true, path))
                    {
                        path.pieces[last] = alone;
                        path.sigma -= whole - alone.sigma;
                        return true;
                    }
                    return false;
                }
            }
            if (now)
            {
                if (StoppedOnChord(rs, time, path, untested, last))
                {
                    return true;
                }
                untested = last + 1;
            }
            return false;
        }

        // Whether a surface stops the pieces `from` to `to` of the path, taken as the chord from the start of the first to
        // the end of the last; the path is then cut at the piece that the surface is on.
        private bool StoppedOnChord(VolumeServices rs, Float time, CurvedPath path, int from, int to)
        {
            Point3f a = path.pieces[from].Point(0), b = path.pieces[to].Point(path.pieces[to].sigma);
            Float length = Distance(a, b);
            if (!(length > 0))
            {
                return false;
            }
            Ray chord = new Ray(a, (b - a) / length, time);
            Point3f P;
            Float distance;
            VolumeGeometry g;
            if (!rs.GetNearestHit(chord, out P, out distance, out g) || !(distance <= length * (1 + 1e-4f)))
            {
                return false;
            }
            // The piece that the surface is on, by how far along the chord its ends are
            Float before = 0;
            for (int k = from; k <= to; ++k)
            {
                RefractivePiece piece = path.pieces[k];
                Float after = k == to ? length : Dot(piece.Point(piece.sigma) - a, chord.d);
                if (distance <= after || k == to)
                {
                    double at = after > before ? piece.sigma * Math.Min(Math.Max((distance - before) / (after - before), 0), 1) : 0;
                    double vx = piece.vx + at * piece.ax, vy = piece.vy + at * piece.ay, vz = piece.vz + at * piece.az;
                    for (int gone = path.pieces.Count - 1; gone > k; --gone)
                    {
                        path.sigma -= path.pieces[gone].sigma;
                        path.pieces.RemoveAt(gone);
                    }
                    path.sigma -= piece.sigma - at;
                    piece.sigma = at;
                    path.pieces[k] = piece;
                    path.hit = true;
                    path.surface = g;
                    path.P = P;
                    path.arrival = chord;
                    path.indexAtEnd = Math.Sqrt(vx * vx + vy * vy + vz * vz);
                    return true;
                }
                before = after;
            }
            return false;
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
                if (sagitta > refractive.surfaceTolerance)
                {
                    chords = Math.Max(chords, Math.Min((int)Math.Ceiling(Math.Sqrt(sagitta / refractive.surfaceTolerance)), 1024));
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
                // (the two points drawn jointly are connected as the field bends the way between them, though it has left
                // this ray straight: where the ray has pieces of a path to draw the first one on)
                bool jointHere = refractive.bentJoint && Joint() && path.pieces.Count > 0 && path.hit;
                // (the volume of the held medium does not draw them itself: it would connect them along a straight line,
                // which the field may bend -- a lamp beside a scattering lens came out 1.2 % too bright that way)
                SampledSpectrum jointly = jointHere ? JointShareBent(rs, path, wi) : new SampledSpectrum(0f);
                m_ctx.medium = refractive.inner;
                m_ctx.jointElsewhere = true;
                bool straight = Create(m_ctx, refractive.inner).Integrate(rs, wi, out L, out transmittance, out weight, out P, out wo, out g);
                m_ctx.jointElsewhere = false;
                m_ctx.medium = saved;
                L += jointly;
                if (!jointHere && g != null && g.joint != null)
                {
                    // (and its collision is not one that two points are drawn jointly from)
                    g.joint.jointly = false;
                }
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
            // (the two points drawn jointly, before the walk: the densities of the path so far are the walk's to change)
            SampledSpectrum jointShare = refractive.bentJoint && Joint() ? JointShareBent(rs, path, wi) : new SampledSpectrum(0f);
            CurvedRayMedium curve = new CurvedRayMedium(refractive.inner, path, wi.time, AlongCurveSettings());
            AlongCurve along = new AlongCurve();
            along.renderer = rs;
            along.path = path;
            along.curve = curve;
            along.medium = medium;
            along.rng = rs.rng;
            // (the collision that the ray leaves, where one more scattering was put before a light: its densities at the
            // points of this ray are those of the points of the scene, by the spread of the path there)
            VolumeJoint joint = m_ctx.joint;
            JointOnCurve rivals = null;
            if (joint != null)
            {
                rivals = new JointOnCurve();
                rivals.joint = joint;
                rivals.curve = curve;
                rivals.path = path;
                rivals.box = refractive.field.Bounds();
                rivals.origin = wi.o;
            }
            m_ctx.jointOnCurve = rivals;
            m_ctx.jointElsewhere = refractive.bentJoint;
            m_ctx.medium = curve;
            Point3f onAxis;
            Ray leaves;
            bool hit = Create(m_ctx, curve).Integrate(along, CurvedRayMedium.Axis(wi.time), out L, out transmittance, out weight, out onAxis, out leaves, out g);
            rs.rng = along.rng;
            m_ctx.jointOnCurve = null;
            m_ctx.jointElsewhere = false;
            m_ctx.medium = saved;
            L += jointShare;
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
                    direct.worldPoint = point;
                    direct.stretch = rivals != null ? rivals.Stretch(onAxis.x) : 1;
                }
                VolumeJoint here = g.joint;
                if (here != null)
                {
                    // (this collision, for the ray that leaves it: where it is in the scene)
                    here.onCurve = true;
                    here.worldC = point;
                    here.worldWo = -tangent;
                    here.curve = curve;
                    if (!refractive.bentJoint)
                    {
                        here.jointly = false;
                    }
                }
                g = new VolumeGeometry();
                g.isSurface = false;
                g.phase = phase;
                g.direct = direct;
                g.joint = here;
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

        // Two scatterings drawn jointly before a light at a position (Volume.JointShare), along the path of a ray: the first,
        // c, on the path, by the density of the straight ray that stands for it about where the curve puts the light; the
        // second, b, about the light in the scene as ever. The way from c to b is searched and walked as the field bends it
        // (ConnectBent, ReachBent), and the area across it per solid angle takes the place of the squared distance, in the
        // estimate and in the densities of the rivals that go by the direction at c (as OnceMoreFinishBent has it).
        private SampledSpectrum JointShareBent(VolumeServices rs, CurvedPath path, Ray wi)
        {
            SampledSpectrum none = new SampledSpectrum(0f);
            if (!m_ctx.mayScatter || !m_ctx.mayScatterTwice || path.lost || path.pieces.Count == 0)
            {
                return none;
            }
            VolumeLight light;
            if (!rs.ChooseLight(m_ctx, out light) || !FixedPoint(light))
            {
                return none;
            }
            // (a ray that the field leaves straight is walked by the volume of the held medium itself, whose densities
            // the collision of the path then has: c is drawn and reached the same way)
            bool bent = path.bent;
            Ray unit = new Ray(wi.o, Normalize(wi.d), wi.time);
            CurvedRayMedium curve = new CurvedRayMedium(refractive.inner, path, wi.time, AlongCurveSettings());
            Ray axis = bent ? CurvedRayMedium.Axis(wi.time) : unit;
            Medium walked = bent ? (Medium)curve : refractive.inner;
            // (the stretch of the path that c is drawn on: the one that the walk of the path records for its collision)
            Medium around = m_ctx.medium;
            m_ctx.medium = walked;
            Float tMin, tMax;
            bool spanned = Create(m_ctx, walked).SpanOf(axis, !path.hit ? Infinity : bent ? (Float)path.sigma : Distance(wi.o, path.P), out tMin, out tMax);
            m_ctx.medium = around;
            if (!spanned || !(tMax > tMin))
            {
                return none;
            }
            InverseDistanceDensity along = InverseDistanceDensity.Create(axis, tMin, tMax, bent ? curve.Pivot(light.point) : light.point);
            if (!along.valid)
            {
                return none;
            }
            Float t = along.Sample(rs.GenerateRandomNumber());
            if (!(t >= tMin) || !(t <= tMax) || !(t > 0))
            {
                return none;
            }
            SampledWavelengths lambda = m_ctx.lambda;
            Vector3f velocity = unit.d;
            Point3f c = bent ? curve.At(t, out velocity) : unit.Apply(t);
            Float indexC = Length(velocity);
            Vector3f tangent = velocity / indexC;
            // The second: the direction by its angle to that of the light, the distance equiangular about the light
            Vector3f toLight = light.point - c;
            Float far = Length(toLight);
            if (!(far > 0))
            {
                return none;
            }
            Vector3f towards = toLight / far, v1, v2;
            CoordinateSystem(towards, &v1, &v2);
            Float theta = Pi * (1 - SafeSqrt(1 - rs.GenerateRandomNumber())), phi = 2 * Pi * rs.GenerateRandomNumber();
            Float sin = std.sin(theta), cos = std.cos(theta);
            Vector3f straight = sin * std.cos(phi) * v1 + sin * std.sin(phi) * v2 + cos * towards;
            Float s = far * cos + far * sin * std.tan(rs.GenerateRandomNumber() * (Pi - theta) + theta - Pi / 2);
            if (!(s > 0) || IsInf(s))
            {
                return none;
            }
            Point3f b = c + s * straight;
            Float squared = LengthSquared(light.point - b);
            Float pdf = squared > 0 ? along.PDF(t) * far / (Pi * Pi * Pi * squared) : 0;
            if (!(pdf > 0))
            {
                return none;
            }
            // The walk to c along the path
            AlongCurve services = new AlongCurve();
            services.renderer = rs;
            services.path = path;
            services.curve = curve;
            services.medium = medium;
            services.rng = rs.rng;
            Medium saved = m_ctx.medium;
            m_ctx.medium = walked;
            SampledSpectrum toC, densitiesC, nullsC;
            bool reached = bent ? Create(m_ctx, curve).ReachTo(services, axis, t, out toC, out densitiesC, out nullsC)
                                : Create(m_ctx, walked).ReachTo(rs, axis, t, out toC, out densitiesC, out nullsC);
            if (bent)
            {
                rs.rng = services.rng;
            }
            m_ctx.medium = saved;
            if (!reached)
            {
                return none;
            }
            MediumProperties mp = medium.SamplePoint(c, lambda, wi.time);
            if (!(bool)mp.sigma_s)
            {
                return none;
            }
            // The way from c to b
            MediumInteraction atC = new MediumInteraction(c, -tangent, wi.time, medium, mp.phase);
            Vector3f w;
            Float searches, spread;
            if (!rs.ConnectBent(m_ctx, atC, null, b, out w, out searches, out spread) || !(spread > 0))
            {
                return none;
            }
            SampledSpectrum toB, densitiesB, nullsB;
            Float equiangular, indexB, seen;
            Vector3f arrival;
            if (!ReachBent(rs, new Ray(c, w, wi.time), b, light.point, out toB, out densitiesB, out nullsB, out equiangular, out arrival, out indexB, out seen))
            {
                return none;
            }
            MediumProperties at = medium.SamplePoint(b, lambda, wi.time);
            if (!(bool)at.sigma_s)
            {
                return none;
            }
            // The densities of the rivals for these two points
            bool pathWeights = !(medium.volumeIntegrator.tracker is DecompositionTracker);
            Float phase = mp.phase.PDF(-tangent, w);
            Float productC = ((mp.sigma_a + mp.sigma_s) * (pathWeights ? nullsC : new SampledSpectrum(1f))).Average() * indexC;
            Float productB = ((at.sigma_a + at.sigma_s) * (pathWeights ? nullsB : new SampledSpectrum(1f))).Average() * indexB;
            if (medium.volumeIntegrator.distanceSampling == VolumeDistanceSampling.Equiangular && equiangular > 0)
            {
                productB = 0;
            }
            Float exchange = s * s / (spread * indexB);
            Float onceMore = OnceMoreScattering.Density(medium.volumeIntegrator.onceMore, mp.phase, -tangent, c, b, light.point);
            Float share = pdf / (pdf + productC * (phase * (productB + equiangular) * exchange + onceMore));
            Point3f savedP = m_ctx.P;
            m_ctx.SetP(b);
            SampledSpectrum result = none;
            MediumInteraction atB = new MediumInteraction(b, -arrival, wi.time, medium, at.phase);
            Vector3f wl;
            SampledSpectrum arriving, beam, beamR;
            if (rs.EvaluateLight(m_ctx, light, null, atB, out wl, out arriving, out beam, out beamR))
            {
                SampledSpectrum r = m_ctx.r * densitiesC * densitiesB * beamR;
                if ((bool)r)
                {
                    result = toC * mp.sigma_s * mp.phase.pSpectral(-tangent, w) * toB * at.sigma_s * at.phase.pSpectral(-arrival, wl) * arriving * beam *
                             (indexC * share * searches * seen * (bent ? Seen(path.indexAtStart, indexC) : 1) * s * s / (spread * pdf * r.Average()));
                }
            }
            m_ctx.SetP(savedP);
            return result;
        }

        // The stretch of a path from a point in a direction up to the point b that the path passes through (the direction
        // was searched for it): the transmittance and the densities of the walk to it (Reach), the density of the
        // equiangular point there about the point of a light, each per sigma, with the direction of arrival, the index
        // there and how much of what is there is seen from the start
        public override bool ReachBent(VolumeServices rs, Ray from, Point3f b, Point3f lightPoint, out SampledSpectrum through, out SampledSpectrum densities,
                                       out SampledSpectrum nulls, out Float equiangular, out Vector3f arrival, out Float index, out Float seen)
        {
            through = densities = nulls = new SampledSpectrum(0f);
            equiangular = 0;
            arrival = from.d;
            index = 1;
            seen = 1;
            CurvedPath path = Trace(rs, from);
            if (path.lost || path.pieces.Count == 0)
            {
                return false;
            }
            // Where the path is nearest to b
            double best = double.PositiveInfinity, bestSigma = 0, before = 0;
            for (int k = 0; k < path.pieces.Count; ++k)
            {
                RefractivePiece piece = path.pieces[k];
                double limit = double.IsInfinity(piece.sigma) ? 1e30 : piece.sigma;
                double sigma = 0;
                for (int iteration = 0; iteration < 8; ++iteration)
                {
                    // (the sigma where the path is across the line to b: Newton's method on (x - b) . v)
                    double x = piece.x + sigma * (piece.vx + 0.5 * sigma * piece.ax) - b.x, y = piece.y + sigma * (piece.vy + 0.5 * sigma * piece.ay) - b.y;
                    double z = piece.z + sigma * (piece.vz + 0.5 * sigma * piece.az) - b.z;
                    double vx = piece.vx + sigma * piece.ax, vy = piece.vy + sigma * piece.ay, vz = piece.vz + sigma * piece.az;
                    double value = x * vx + y * vy + z * vz, slope = vx * vx + vy * vy + vz * vz + x * piece.ax + y * piece.ay + z * piece.az;
                    if (!(slope > 0))
                    {
                        break;
                    }
                    sigma = Math.Min(Math.Max(sigma - value / slope, 0), limit);
                }
                Point3f nearest = piece.Point(sigma);
                double miss = Distance(nearest, b);
                if (miss < best)
                {
                    best = miss;
                    bestSigma = before + sigma;
                }
                before += limit;
            }
            Float far = Distance(from.o, b);
            if (!(best <= 1e-3f * far + 1e-5f * (1 + Length((Vector3f)b))))
            {
                return false;
            }
            Medium saved = m_ctx.medium;
            if (!path.bent)
            {
                // (a stretch that the field leaves straight: by the volume of the held medium itself, as the walk of a
                // path along it is, so that the densities of the two are of one kind)
                Ray unit = new Ray(from.o, Normalize(from.d), from.time);
                m_ctx.medium = refractive.inner;
                bool straight = Create(m_ctx, refractive.inner).ReachTo(rs, unit, far, out through, out densities, out nulls);
                m_ctx.medium = saved;
                if (!straight)
                {
                    return false;
                }
                arrival = unit.d;
                if (refractive.inner.volumeIntegrator.distanceSampling != VolumeDistanceSampling.Density)
                {
                    EquiangularDensity beside = EquiangularDensity.Create(unit, 0, path.hit ? Distance(from.o, path.P) : Infinity, lightPoint);
                    if (beside.valid)
                    {
                        equiangular = beside.PDF(far);
                    }
                }
                return true;
            }
            CurvedRayMedium curve = new CurvedRayMedium(refractive.inner, path, from.time, AlongCurveSettings());
            AlongCurve along = new AlongCurve();
            along.renderer = rs;
            along.path = path;
            along.curve = curve;
            along.medium = medium;
            along.rng = rs.rng;
            m_ctx.medium = curve;
            Ray axis = CurvedRayMedium.Axis(from.time);
            bool reached = Create(m_ctx, curve).ReachTo(along, axis, (Float)bestSigma, out through, out densities, out nulls);
            rs.rng = along.rng;
            m_ctx.medium = saved;
            if (!reached)
            {
                return false;
            }
            Vector3f velocity;
            curve.At(bestSigma, out velocity);
            index = Length(velocity);
            arrival = velocity / index;
            seen = Seen(path.indexAtStart, index);
            if (refractive.inner.volumeIntegrator.distanceSampling != VolumeDistanceSampling.Density)
            {
                EquiangularDensity about = EquiangularDensity.Create(axis, 0, path.hit ? (Float)path.sigma : Infinity, curve.Pivot(lightPoint));
                if (about.valid)
                {
                    equiangular = about.PDF((Float)bestSigma);
                }
            }
            return true;
        }

        // The emission and the transmittance along the path of a ray, as Transmit follows it
        public override SampledSpectrum Gather(VolumeServices rs, Ray entering, Float tMax, bool gather, SampledSpectrum density, Bounds3f further,
                                               out SampledSpectrum emitted, out bool bent, out bool hit, out Point3f P, out VolumeGeometry g, out Ray arrival)
        {
            CurvedPath path = Trace(rs, entering);
            Medium saved = m_ctx.medium;
            bent = path.bent;
            hit = path.hit;
            P = path.P;
            g = path.surface;
            arrival = path.arrival;
            emitted = new SampledSpectrum(0f);
            if (!path.bent)
            {
                m_ctx.medium = refractive.inner;
                Volume held = Create(m_ctx, refractive.inner);
                Point3f end = entering.Apply(tMax);
                if (gather)
                {
                    emitted = held.EmissionOver(rs, entering.o, end, density);
                }
                Float t0, t1;
                SampledSpectrum straight = further.IntersectP(end, entering.d, Infinity, &t0, &t1) ? held.Transmittance(rs, entering.o, end) : new SampledSpectrum(0f);
                m_ctx.medium = saved;
                return straight;
            }
            if (path.lost)
            {
                return new SampledSpectrum(0f);
            }
            CurvedRayMedium curve = new CurvedRayMedium(refractive.inner, path, entering.time, AlongCurveSettings());
            AlongCurve along = new AlongCurve();
            along.renderer = rs;
            along.path = path;
            along.curve = curve;
            along.medium = medium;
            along.rng = rs.rng;
            m_ctx.medium = curve;
            Volume onCurve = Create(m_ctx, curve);
            Point3f far = new Point3f(path.hit ? (Float)path.sigma : Unbounded, 0, 0);
            if (gather)
            {
                emitted = onCurve.EmissionOver(along, new Point3f(0, 0, 0), far, density);
            }
            Float u0, u1;
            SampledSpectrum T = path.hit && further.IntersectP(path.P, path.arrival.d, Infinity, &u0, &u1) ? onCurve.Transmittance(along, new Point3f(0, 0, 0), far)
                                                                                                        : new SampledSpectrum(0f);
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
