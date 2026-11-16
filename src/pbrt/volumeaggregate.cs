// (not in pbrt-v4) volumeaggregate.cs
//
// The volume aggregate of J. Fong, M. Wrenninge, C. Kulla and R. Habel, "Production Volume Rendering" (SIGGRAPH 2017 course),
// section 6: many volumes that share space, integrated as one without their boundaries.
//
//     class VolumeAggregate {                      class Volume {
//         class Iterator {                             virtual float extinction(const Point &p, float time, float filterWidth);
//             float start() const;                     virtual std::pair<float, float> extinctionMinMax(const Region &r);
//             float end() const;                       virtual PhaseFunction* phaseFunction();
//             float maxExtinction() const;         };
//             float minExtinction() const;
//             bool finished() const;
//             void operator++();
//             int numVolumes() const;
//             const Volume* volumes() const;
//         }
//         void insert(const Volume *v);
//         Iterator iterator(const Ray &ray) const;
//     };
//
// The structure (section 6.8). An octree spans the region of all the volumes. Each node has the list of the volumes that
// overlap it (even partly) and the largest and smallest extinction over it. A node is divided while
// (max(R) - min(R)) * diag(R) > T (T between 1 and 4 in the course), whatever the boundaries of the volumes are, and (section
// 6.11) while it is larger than a volume that it holds. A ray walks the leaves in order: the intervals of integration are the
// nodes, not the volumes, so their number does not grow with the number of volumes crossed.
//
// The volumes answer what listing 25 asks. extinction() at a point is Medium.SamplePoint(); extinctionMinMax() over a region
// is Medium.AggregateRegion() with Medium.AggregateExtinction(): the grid of majorants that each medium of pbrt-v4 has, read
// over the region, and, for the media that have one, the grid of the smallest densities (MajorantGrid.minima): the minimum of
// a node is the sum over the volumes that wholly cover it of the smallest density of each there. Nothing is filtered (pbrt's
// media are point sampled: no filter width, and the meta-properties are computed once, when the scene is made, not on the fly).
//
// Kutz et al. 2017 in the aggregate ("tracker" "decomposition", "spectral" "tracking" of the aggregate medium): the control
// component of a node is those smallest densities with the coefficients of their volumes; a collision of it belongs to one
// volume and needs no lookup of any. Spectral tracking chooses among the scatterings of the volumes and the null collision.
//
// Here the aggregate is a medium, `MakeNamedMedium "name" "string type" "aggregate" "string members" [ ... ]`, of media that
// have an extent of their own (grid, rgbgrid, nanovdb, cloud, texture) and that were made before it. The members need no
// geometry. A ray is in the aggregate the way it is in any medium (the camera medium, the inside of a surface). In the
// "production" path tracer the aggregate is a volume that integrates itself, AggregateVolume: the tracking of listing 26 over
// the iterator, for any number of scatterings, with the collision given to one member chosen by its scattering coefficient
// (section 3.9). The other integrators see it as the sum of its members, each within its bounds.

using System;
using System.Collections.Generic;

namespace pbrt
{
    using static pbrt;

    // AggregateMember Definition: a volume of an aggregate with what it says of itself
    public sealed unsafe class AggregateMember
    {
        public AggregateMember(Medium medium, FileLoc loc)
        {
            this.medium = medium;
            if (!medium.AggregateRegion(out mediumBounds, out renderFromMedium, out grid))
            {
                ErrorExit(loc, StringPrintf("%s: a medium without an extent of its own cannot be in an aggregate.", medium));
            }
            renderBounds = renderFromMedium.Apply(mediumBounds);
            // The largest extinction of density 1 over the visible wavelengths, for the heuristic of the subdivision
            scalarExtinction = 0;
            for (int i = 0; i < 16; ++i)
            {
                SampledSpectrum sigma = medium.AggregateExtinction(SampledWavelengths.SampleUniform((i + 0.5f) / 16));
                scalarExtinction = std.max(scalarExtinction, sigma.MaxComponentValue());
            }
            // The size of a cell of the grid: a node smaller than it learns nothing more
            Float extent = Length(renderBounds.Diagonal());
            int finest = grid != null ? std.max(grid.res.x, std.max(grid.res.y, grid.res.z)) : 1;
            cellSize = extent / finest;
        }

        public bool Inside(Point3f p)
        {
            return pbrt.Inside(renderFromMedium.ApplyInverse(p), mediumBounds);
        }

        // The largest density over a box of render space (extinctionMinMax of listing 25; the minimum is not known)
        public Float MaxDensity(Bounds3f box)
        {
            if (!Overlaps(box, renderBounds))
            {
                return 0;
            }
            if (grid == null)
            {
                return 1;
            }
            Point3f lo = new Point3f(Infinity, Infinity, Infinity), hi = new Point3f(-Infinity, -Infinity, -Infinity);
            for (int corner = 0; corner < 8; ++corner)
            {
                Point3f p = renderFromMedium.ApplyInverse(box.Corner(corner));
                lo = Min(lo, p);
                hi = Max(hi, p);
            }
            Vector3f a = mediumBounds.Offset(lo), b = mediumBounds.Offset(hi);
            int* first = stackalloc int[3];
            int* last = stackalloc int[3];
            for (int axis = 0; axis < 3; ++axis)
            {
                if (b[axis] < 0 || a[axis] > 1)
                {
                    return 0;
                }
                int n = grid.res[axis];
                first[axis] = Clamp((int)std.floor(a[axis] * n), 0, n - 1);
                last[axis] = Clamp((int)std.floor(b[axis] * n), 0, n - 1);
            }
            Float largest = 0;
            for (int z = first[2]; z <= last[2]; ++z)
            {
                for (int y = first[1]; y <= last[1]; ++y)
                {
                    for (int x = first[0]; x <= last[0]; ++x)
                    {
                        largest = std.max(largest, grid.Lookup(x, y, z));
                    }
                }
            }
            return largest;
        }

        // The smallest density over a box of render space, where the volume knows it (a grid of minima) and the box is
        // wholly inside the volume; zero otherwise
        public Float MinDensity(Bounds3f box)
        {
            if (grid == null || grid.minima == null)
            {
                return 0;
            }
            Point3f lo = new Point3f(Infinity, Infinity, Infinity), hi = new Point3f(-Infinity, -Infinity, -Infinity);
            for (int corner = 0; corner < 8; ++corner)
            {
                Point3f p = renderFromMedium.ApplyInverse(box.Corner(corner));
                if (!pbrt.Inside(p, mediumBounds))
                {
                    return 0;
                }
                lo = Min(lo, p);
                hi = Max(hi, p);
            }
            Vector3f a = mediumBounds.Offset(lo), b = mediumBounds.Offset(hi);
            int* first = stackalloc int[3];
            int* last = stackalloc int[3];
            for (int axis = 0; axis < 3; ++axis)
            {
                int n = grid.res[axis];
                first[axis] = Clamp((int)std.floor(a[axis] * n), 0, n - 1);
                last[axis] = Clamp((int)std.floor(b[axis] * n), 0, n - 1);
            }
            Float smallest = Infinity;
            for (int z = first[2]; z <= last[2]; ++z)
            {
                for (int y = first[1]; y <= last[1]; ++y)
                {
                    for (int x = first[0]; x <= last[0]; ++x)
                    {
                        smallest = std.min(smallest, grid.minima[x + grid.res.x * (y + grid.res.y * z)]);
                    }
                }
            }
            return IsInf(smallest) ? 0 : smallest;
        }

        // The mean density over a box of render space, by the means of the cells of the grid that it touches (zero where the
        // box is not wholly in the volume or the means are not known): a control for the estimators that take one
        // (volumeaggregate2.cs), which any value serves without bias
        public Float MeanDensity(Bounds3f box)
        {
            if (grid == null || grid.averages == null)
            {
                return 0;
            }
            Point3f lo = new Point3f(Infinity, Infinity, Infinity), hi = new Point3f(-Infinity, -Infinity, -Infinity);
            for (int corner = 0; corner < 8; ++corner)
            {
                Point3f p = renderFromMedium.ApplyInverse(box.Corner(corner));
                if (!pbrt.Inside(p, mediumBounds))
                {
                    return 0;
                }
                lo = Min(lo, p);
                hi = Max(hi, p);
            }
            Vector3f a = mediumBounds.Offset(lo), b = mediumBounds.Offset(hi);
            int* first = stackalloc int[3];
            int* last = stackalloc int[3];
            for (int axis = 0; axis < 3; ++axis)
            {
                int n = grid.res[axis];
                first[axis] = Clamp((int)std.floor(a[axis] * n), 0, n - 1);
                last[axis] = Clamp((int)std.floor(b[axis] * n), 0, n - 1);
            }
            Float sum = 0;
            int count = 0;
            for (int z = first[2]; z <= last[2]; ++z)
            {
                for (int y = first[1]; y <= last[1]; ++y)
                {
                    for (int x = first[0]; x <= last[0]; ++x)
                    {
                        sum += grid.averages[x + grid.res.x * (y + grid.res.y * z)];
                        ++count;
                    }
                }
            }
            return count > 0 ? sum / count : 0;
        }

        public Medium medium;
        public Bounds3f mediumBounds, renderBounds;
        public Transform renderFromMedium;
        public MajorantGrid grid;
        public Float scalarExtinction, cellSize;
    }

    // VolumeAggregate Definition
    public sealed unsafe class VolumeAggregate
    {
        // A node of the octree: its box, the volumes that overlap it with the largest density of each over the box, and the
        // largest and smallest extinction over it. firstChild is -1 for a leaf.
        public sealed class Node
        {
            public Bounds3f box;
            public int firstChild = -1;
            public int[] volumes;
            public Float[] maxDensity, minDensity, meanDensity;
            public Float maxExtinction, minExtinction;
        }

        public VolumeAggregate(Float threshold, int maxDepth)
        {
            this.threshold = threshold;
            this.maxDepth = maxDepth;
        }

        // insert() of the course. The octree is built by Build() once every volume is in.
        public void Insert(AggregateMember volume)
        {
            members.Add(volume);
        }

        public void Build()
        {
            nodes.Clear();
            Bounds3f all = new Bounds3f();
            int[] every = new int[members.Count];
            for (int i = 0; i < members.Count; ++i)
            {
                all = Union(all, members[i].renderBounds);
                every[i] = i;
            }
            Node root = new Node();
            root.box = all;
            nodes.Add(root);
            leaves = 0;
            deepest = 0;
            Divide(0, 0, every);
        }

        private void Divide(int index, int depth, int[] candidates)
        {
            Node node = nodes[index];
            List<int> held = new List<int>();
            List<Float> densities = new List<Float>(), minima = new List<Float>(), means = new List<Float>();
            Float largest = 0, smallest = 0, smallestVolume = Infinity, finestCell = Infinity;
            for (int k = 0; k < candidates.Length; ++k)
            {
                AggregateMember member = members[candidates[k]];
                if (!Overlaps(node.box, member.renderBounds))
                {
                    continue;
                }
                Float density = member.MaxDensity(node.box);
                held.Add(candidates[k]);
                densities.Add(density);
                Float least = member.MinDensity(node.box);
                minima.Add(least);
                means.Add(std.min(density, std.max(least, member.MeanDensity(node.box))));
                largest += density * member.scalarExtinction;
                smallest += least * member.scalarExtinction;
                smallestVolume = std.min(smallestVolume, Length(member.renderBounds.Diagonal()));
                finestCell = std.min(finestCell, member.cellSize);
            }
            node.volumes = held.ToArray();
            node.maxDensity = densities.ToArray();
            node.minDensity = minima.ToArray();
            node.meanDensity = means.ToArray();
            node.maxExtinction = largest;
            node.minExtinction = smallest;
            deepest = Math.Max(deepest, depth);
            Float diagonal = Length(node.box.Diagonal());
            // The heuristic of section 6.8, and the forced division of a node larger than a volume it holds (section 6.11).
            // A node smaller than the cells that the volumes know their densities by is not divided: its children would be
            // told the same.
            bool divide = held.Count > 0 && depth < maxDepth && diagonal > finestCell &&
                          ((node.maxExtinction - node.minExtinction) * diagonal > threshold || diagonal > smallestVolume);
            if (!divide)
            {
                ++leaves;
                return;
            }
            node.firstChild = nodes.Count;
            Point3f centre = node.box.Lerp(new Point3f(0.5f, 0.5f, 0.5f));
            for (int child = 0; child < 8; ++child)
            {
                Node made = new Node();
                Point3f corner = node.box.Corner(child);
                made.box = new Bounds3f(corner, centre);
                nodes.Add(made);
            }
            for (int child = 0; child < 8; ++child)
            {
                Divide(node.firstChild + child, depth + 1, node.volumes);
            }
        }

        // The leaf that holds a point of the box of the root
        private int Leaf(Point3f p)
        {
            int index = 0;
            while (nodes[index].firstChild >= 0)
            {
                Node node = nodes[index];
                Point3f centre = node.box.Lerp(new Point3f(0.5f, 0.5f, 0.5f));
                // (Bounds3f.Corner: bit 0 is x, bit 1 is y, bit 2 is z, set for the upper side)
                int child = (p.x > centre.x ? 1 : 0) | (p.y > centre.y ? 2 : 0) | (p.z > centre.z ? 4 : 0);
                index = node.firstChild + child;
            }
            return index;
        }

        // Iterator Definition: the leaves that a ray crosses, in order, up to tMax (the direction of the ray has unit length).
        // Next() is the course's operator++ with finished(): it moves to the next interval and says whether there is one.
        public struct Iterator
        {
            internal Iterator(VolumeAggregate aggregate, Point3f o, Vector3f d, Float tMax)
            {
                this.aggregate = aggregate;
                this.o = o;
                this.d = d;
                node = null;
                start = end = 0;
                Float t0 = 0, t1 = 0;
                if (aggregate.nodes.Count == 0 || !aggregate.nodes[0].box.IntersectP(o, d, tMax, &t0, &t1))
                {
                    t = tEnd = 0;
                    nudge = 0;
                    return;
                }
                t = t0;
                tEnd = t1;
                nudge = 1e-5f * Length(aggregate.nodes[0].box.Diagonal());
            }

            public bool Next()
            {
                if (!(t < tEnd))
                {
                    return false;
                }
                Float step = std.min(nudge, 0.5f * (tEnd - t));
                node = aggregate.nodes[aggregate.Leaf(o + (t + step) * d)];
                Float t0 = 0, t1 = 0;
                Float leave = node.box.IntersectP(o, d, Infinity, &t0, &t1) ? t1 : t + 2 * step;
                if (!(leave > t + step))
                {
                    leave = t + 2 * step;
                }
                start = t;
                end = std.min(leave, tEnd);
                t = end;
                return true;
            }

            public Float Start() { return start; }
            public Float End() { return end; }
            public Float MaxExtinction() { return node.maxExtinction; }
            public Float MinExtinction() { return node.minExtinction; }
            public int NumVolumes() { return node.volumes.Length; }
            public int[] Volumes() { return node.volumes; }
            // The largest density of the k-th volume of the node over the node
            public Float MaxDensity(int k) { return node.maxDensity[k]; }
            // The smallest density of the k-th volume of the node over the node (zero where it is not known)
            public Float MinDensity(int k) { return node.minDensity[k]; }
            // The mean density of the k-th volume of the node over the node (between the two; zero where it is not known)
            public Float MeanDensity(int k) { return node.meanDensity[k]; }

            private VolumeAggregate aggregate;
            private Point3f o;
            private Vector3f d;
            private Float t, tEnd, nudge, start, end;
            private Node node;
        }

        public Iterator GetIterator(Point3f o, Vector3f d, Float tMax)
        {
            return new Iterator(this, o, d, tMax);
        }

        // The volumes of the leaf that holds a point; null outside the box of the aggregate
        public int[] VolumesAt(Point3f p)
        {
            if (nodes.Count == 0 || !Inside(p, nodes[0].box))
            {
                return null;
            }
            return nodes[Leaf(p)].volumes;
        }

        public AggregateMember Member(int index) { return members[index]; }
        public int MemberCount() { return members.Count; }

        // A bound of the extinction of the volumes together over a box of rendering space: the sum of the bounds of those
        // that the box meets (for a refractive medium that holds the aggregate; no control). False if a volume cannot say.
        public bool MajorantOver(Bounds3f box, SampledWavelengths lambda, out SampledSpectrum sigma_maj)
        {
            sigma_maj = new SampledSpectrum(0f);
            for (int i = 0; i < members.Count; ++i)
            {
                if (!Overlaps(box, members[i].renderBounds))
                {
                    continue;
                }
                SampledSpectrum bound;
                Float control, average;
                if (!members[i].medium.MajorantOver(box, lambda, out bound, out control, out average))
                {
                    return false;
                }
                sigma_maj += bound;
            }
            return true;
        }
        public int NodeCount() { return nodes.Count; }
        public int LeafCount() { return leaves; }
        public int Depth() { return deepest; }
        public Node NodeAt(int index) { return nodes[index]; }

        private List<AggregateMember> members = new List<AggregateMember>();
        private List<Node> nodes = new List<Node>();
        private Float threshold;
        private int maxDepth, leaves, deepest;
    }

    // BoundedMedium Definition: a member of an aggregate as the integrators that do not know the aggregate see it. Nothing
    // outside of its bounds (a medium of pbrt-v4 leaves that to the ray that is cut to its box).
    public sealed unsafe class BoundedMedium : Medium, IMajorantMedium<RayMajorantIterator>
    {
        public BoundedMedium(AggregateMember member)
        {
            this.member = member;
            volumeIntegrator = member.medium.volumeIntegrator;
        }

        public override string ToString() { return StringPrintf("[ BoundedMedium medium: %s ]", member.medium); }

        public override bool IsEmissive() { return member.medium.IsEmissive(); }

        public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda)
        {
            return SamplePoint(p, lambda, 0);
        }

        public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda, Float time)
        {
            MediumProperties mp = member.medium.SamplePoint(p, lambda, time);
            if (!member.Inside(p))
            {
                mp.sigma_a = new SampledSpectrum(0f);
                mp.sigma_s = new SampledSpectrum(0f);
                mp.Le = new SampledSpectrum(0f);
            }
            return mp;
        }

        public RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda)
        {
            return SampleRay(ray, tMax, lambda, new ScratchBuffer());
        }

        public override RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda, ScratchBuffer buf)
        {
            return member.medium.SampleRay(ray, tMax, lambda, buf);
        }

        private AggregateMember member;
    }

    // AggregateMedium Definition
    public sealed unsafe class AggregateMedium : Medium, IMajorantMedium<RayMajorantIterator>
    {
        public AggregateMedium(Medium[] media, Float threshold, int maxDepth, FileLoc loc)
        {
            aggregate = new VolumeAggregate(threshold, maxDepth);
            Medium[] bounded = new Medium[media.Length];
            int[] ranks = new int[media.Length];
            for (int i = 0; i < media.Length; ++i)
            {
                AggregateMember member = new AggregateMember(media[i], loc);
                aggregate.Insert(member);
                bounded[i] = new BoundedMedium(member);
                emissive |= media[i].IsEmissive();
            }
            aggregate.Build();
            sum = new CompositeMedium(bounded, ranks);
        }

        public static AggregateMedium Create(Medium[] media, ParameterDictionary parameters, FileLoc loc)
        {
            if (media.Length == 0)
            {
                ErrorExit(loc, "An \"aggregate\" medium needs \"string members\".");
            }
            Float threshold = parameters.GetOneFloat("threshold", 2);
            int maxDepth = parameters.GetOneInt("maxdepth", 6);
            if (!(threshold > 0) || maxDepth < 0 || maxDepth > 10)
            {
                ErrorExit(loc, "\"threshold\" must be positive and \"maxdepth\" between 0 and 10.");
            }
            return new AggregateMedium(media, threshold, maxDepth, loc);
        }

        public override string ToString()
        {
            return StringPrintf("[ AggregateMedium volumes: %d nodes: %d leaves: %d depth: %d ]", aggregate.MemberCount(), aggregate.NodeCount(),
                                aggregate.LeafCount(), aggregate.Depth());
        }

        public override bool IsEmissive() { return emissive; }

        public VolumeAggregate Aggregate() { return aggregate; }

        public override bool MajorantOver(Bounds3f box, SampledWavelengths lambda, out SampledSpectrum sigma_maj, out Float controlFraction, out Float averageFraction)
        {
            controlFraction = 0;
            averageFraction = 0;
            return aggregate.MajorantOver(box, lambda, out sigma_maj);
        }

        // The emission of the aggregate as one light (media_emission_light.cs): the box of its members in render space, and
        // over a box of it the sum of what the members say of their own
        public override bool EmissionRegion(out Bounds3f mediumBounds, out Transform renderFromMedium)
        {
            Bounds3f all = new Bounds3f();
            for (int i = 0; i < aggregate.MemberCount(); ++i)
            {
                all = Union(all, aggregate.Member(i).renderBounds);
            }
            mediumBounds = all;
            renderFromMedium = new Transform();
            emissionBounds = all;
            return aggregate.MemberCount() > 0;
        }

        public override Float EmissionBoundUnit(Bounds3f unitBox)
        {
            Bounds3f renderBox = new Bounds3f(emissionBounds.Lerp(unitBox.pMin), emissionBounds.Lerp(unitBox.pMax));
            Float sum = 0;
            for (int i = 0; i < aggregate.MemberCount(); ++i)
            {
                AggregateMember member = aggregate.Member(i);
                if (!member.medium.IsEmissive() || !Overlaps(renderBox, member.renderBounds))
                {
                    continue;
                }
                // The box in the unit cube of the member: the box of its corners there
                Bounds3f inMember = new Bounds3f();
                for (int corner = 0; corner < 8; ++corner)
                {
                    inMember = Union(inMember, (Point3f)member.mediumBounds.Offset(member.renderFromMedium.ApplyInverse(renderBox.Corner(corner))));
                }
                Bounds3f clipped = new Bounds3f(Max(inMember.pMin, new Point3f(0, 0, 0)), Min(inMember.pMax, new Point3f(1, 1, 1)));
                if (clipped.pMin.x > clipped.pMax.x || clipped.pMin.y > clipped.pMax.y || clipped.pMin.z > clipped.pMax.z)
                {
                    continue;
                }
                sum += std.max<Float>(0, member.medium.EmissionBoundUnit(clipped));
            }
            return sum;
        }

        private Bounds3f emissionBounds;

        public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda)
        {
            return sum.SamplePoint(p, lambda, 0);
        }

        public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda, Float time)
        {
            return sum.SamplePoint(p, lambda, time);
        }

        public RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda)
        {
            return SampleRay(ray, tMax, lambda, new ScratchBuffer());
        }

        public override RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda, ScratchBuffer buf)
        {
            return sum.SampleRay(ray, tMax, lambda, buf);
        }

        private VolumeAggregate aggregate;
        private CompositeMedium sum;
        private bool emissive;
    }

    // AggregateVolume Definition: the aggregate integrates itself (the course, listing 26, in the interface of listing 1). A
    // walk of tentative collisions over the intervals of the iterator, each with the majorant of its node: the sum over the
    // volumes of the node of the largest density of each there times its extinction. At a collision every volume of the node
    // is asked at the point; the collision is real with the probability of the summed extinction over the majorant, and a
    // scattering belongs to one volume, chosen by its scattering coefficient, whose phase function goes back to the path
    // tracer with the point. The wavelengths are handled as in the other volumes of productionvolume.cs.
    public sealed unsafe class AggregateVolume : Volume
    {
        public AggregateVolume(VolumeContext ctx, AggregateMedium medium) : base(ctx, medium)
        {
            aggregate = medium.Aggregate();
            extinction = new SampledSpectrum[aggregate.MemberCount()];
            known = new bool[aggregate.MemberCount()];
            unit = new MediumProperties[aggregate.MemberCount()];
            unitState = new int[aggregate.MemberCount()];
            controlA = new SampledSpectrum[aggregate.MemberCount()];
            controlS = new SampledSpectrum[aggregate.MemberCount()];
        }

        // The control component of the interval (decomposition tracking): for each volume of the node that knows its smallest
        // density there and what its coefficients are without a lookup, the coefficients at that density; their sum
        private SampledSpectrum Control(ref VolumeAggregate.Iterator interval)
        {
            SampledSpectrum sigma_c = new SampledSpectrum(0f);
            int[] volumes = interval.Volumes();
            for (int k = 0; k < volumes.Length; ++k)
            {
                controlA[k] = new SampledSpectrum(0f);
                controlS[k] = new SampledSpectrum(0f);
                Float least = interval.MinDensity(k);
                if (!(least > 0))
                {
                    continue;
                }
                int index = volumes[k];
                if (unitState[index] == 0)
                {
                    unitState[index] = aggregate.Member(index).medium.ControlProperties(m_ctx.lambda, out unit[index]) ? 1 : -1;
                }
                if (unitState[index] < 0)
                {
                    continue;
                }
                controlA[k] = least * unit[index].sigma_a;
                controlS[k] = least * unit[index].sigma_s;
                sigma_c += controlA[k] + controlS[k];
            }
            return sigma_c;
        }

        // The extinction of density 1 of a volume, for the wavelengths of the path
        private SampledSpectrum Extinction(int index)
        {
            if (!known[index])
            {
                extinction[index] = aggregate.Member(index).medium.AggregateExtinction(m_ctx.lambda);
                known[index] = true;
            }
            return extinction[index];
        }

        private SampledSpectrum Majorant(ref VolumeAggregate.Iterator interval)
        {
            SampledSpectrum sigma_maj = new SampledSpectrum(0f);
            int[] volumes = interval.Volumes();
            for (int k = 0; k < volumes.Length; ++k)
            {
                sigma_maj += Extinction(volumes[k]) * interval.MaxDensity(k);
            }
            return sigma_maj;
        }

        // What the volumes of the interval are at a point: their summed coefficients, the emission sigma_a Le summed, and the
        // properties of each (those of a volume that the point is not in are left out: index -1)
        private void At(ref VolumeAggregate.Iterator interval, Point3f p, Float time, MediumProperties[] each, out SampledSpectrum sigma_a,
                        out SampledSpectrum sigma_s, out SampledSpectrum emission)
        {
            sigma_a = new SampledSpectrum(0f);
            sigma_s = new SampledSpectrum(0f);
            emission = new SampledSpectrum(0f);
            int[] volumes = interval.Volumes();
            for (int k = 0; k < volumes.Length; ++k)
            {
                AggregateMember member = aggregate.Member(volumes[k]);
                each[k] = default;
                each[k].sigma_s = new SampledSpectrum(0f);
                if (!member.Inside(p))
                {
                    continue;
                }
                each[k] = member.medium.SamplePoint(p, m_ctx.lambda, time);
                sigma_a += each[k].sigma_a;
                sigma_s += each[k].sigma_s;
                if ((bool)each[k].Le)
                {
                    emission += each[k].sigma_a * each[k].Le;
                }
            }
        }

        public override bool Integrate(VolumeServices rs, Ray wi, out SampledSpectrum L, out SampledSpectrum transmittance, out SampledSpectrum weight,
                                       out Point3f P, out Ray wo, out VolumeGeometry g)
        {
            Float distance;
            bool hit = rs.GetNearestHit(wi, out P, out distance, out g);
            transmittance = new SampledSpectrum(1f);
            L = new SampledSpectrum(0f);
            wo = new Ray(P, wi.d, wi.time);
            if (medium.volumeIntegrator.spectralTracking)
            {
                return IntegrateSpectral(rs, wi, hit, distance, out L, out weight, ref P, ref wo, ref g);
            }
            bool decompose = medium.volumeIntegrator.tracker is DecompositionTracker;
            SampledSpectrum beta = new SampledSpectrum(1f), r = m_ctx.r;
            MediumProperties[] each = new MediumProperties[aggregate.MemberCount()];
            VolumeAggregate.Iterator interval = aggregate.GetIterator(wi.o, wi.d, distance);
            while (interval.Next())
            {
                // Skip empty space
                if (interval.NumVolumes() == 0)
                {
                    continue;
                }
                SampledSpectrum sigma_maj = Majorant(ref interval);
                // Decomposition tracking: the control component of the node and the majorant of what is left
                SampledSpectrum sigma_c = decompose ? Control(ref interval) : new SampledSpectrum(0f);
                SampledSpectrum sigma_r = ClampZero(sigma_maj - sigma_c);
                Float pControl = sigma_maj[0] > 0 ? sigma_c[0] / sigma_maj[0] : 0;
                Float t = interval.Start(), tEnd = interval.End();
                while (true)
                {
                    // Take a step
                    Float step = sigma_maj[0] > 0 ? -std.log(1 - rs.GenerateRandomNumber()) / sigma_maj[0] : Infinity;
                    if (!(t + step < tEnd))
                    {
                        // Out of the interval without a collision
                        SampledSpectrum rest = Attenuation(sigma_maj, tEnd - t);
                        beta *= rest / rest[0];
                        r *= rest / rest[0];
                        break;
                    }
                    t += step;
                    SampledSpectrum T_maj = Attenuation(sigma_maj, step);
                    Point3f p = wi.Apply(t);
                    if (pControl > 0 && rs.GenerateRandomNumber() < pControl)
                    {
                        // A collision of the control component: of one volume, by its share, without a lookup
                        int[] members = interval.Volumes();
                        Float uc = rs.GenerateRandomNumber() * sigma_c[0];
                        int member = -1;
                        for (int k = 0; k < members.Length; ++k)
                        {
                            Float share = controlA[k][0] + controlS[k][0];
                            if (share > 0)
                            {
                                member = k;
                                if (uc < share)
                                {
                                    break;
                                }
                                uc -= share;
                            }
                        }
                        if (rs.GenerateRandomNumber() * (controlA[member][0] + controlS[member][0]) < controlA[member][0])
                        {
                            weight = new SampledSpectrum(0f);
                            return false;
                        }
                        Float pdfControl = T_maj[0] * controlS[member][0];
                        beta *= T_maj * controlS[member] / pdfControl;
                        r *= T_maj * controlS[member] / pdfControl;
                        weight = beta;
                        m_ctx.r = r;
                        PhaseFunction phase = unit[members[member]].phase;
                        P = p;
                        g = new VolumeGeometry();
                        g.isSurface = false;
                        g.phase = phase;
                        g.scatter = new MediumInteraction(p, -wi.d, wi.time, medium, phase);
                        wo = new Ray(p, wi.d, wi.time);
                        return (bool)beta && (bool)r;
                    }
                    SampledSpectrum sigma_a, sigma_s, emission;
                    At(ref interval, p, wi.time, each, out sigma_a, out sigma_s, out emission);
                    if (decompose)
                    {
                        // What is left of each volume
                        sigma_a = new SampledSpectrum(0f);
                        sigma_s = new SampledSpectrum(0f);
                        for (int k = 0; k < interval.NumVolumes(); ++k)
                        {
                            each[k].sigma_a = ClampZero(each[k].sigma_a - controlA[k]);
                            each[k].sigma_s = ClampZero(each[k].sigma_s - controlS[k]);
                            sigma_a += each[k].sigma_a;
                            sigma_s += each[k].sigma_s;
                        }
                    }
                    if ((bool)emission)
                    {
                        Float pdfCollision = sigma_r[0] * T_maj[0];
                        SampledSpectrum de = r * T_maj * sigma_r / pdfCollision;
                        if ((bool)de)
                        {
                            L += m_ctx.emissionWeight * beta * T_maj / pdfCollision * emission / de.Average();
                        }
                    }
                    // A real collision or not, by the first wavelength
                    Float u = rs.GenerateRandomNumber() * sigma_r[0];
                    if (u < sigma_a[0])
                    {
                        weight = new SampledSpectrum(0f);
                        return false;
                    }
                    u -= sigma_a[0];
                    if (u < sigma_s[0])
                    {
                        // The collision is with a particle of one volume (section 3.9)
                        int[] volumes = interval.Volumes();
                        int chosen = -1;
                        for (int k = 0; k < volumes.Length; ++k)
                        {
                            if (each[k].sigma_s[0] > 0)
                            {
                                chosen = k;
                                if (u < each[k].sigma_s[0])
                                {
                                    break;
                                }
                                u -= each[k].sigma_s[0];
                            }
                        }
                        MediumProperties mp = each[chosen];
                        Float pdf = T_maj[0] * mp.sigma_s[0];
                        beta *= T_maj * mp.sigma_s / pdf;
                        r *= T_maj * mp.sigma_s / pdf;
                        weight = beta;
                        m_ctx.r = r;
                        P = p;
                        g = new VolumeGeometry();
                        g.isSurface = false;
                        g.phase = mp.phase;
                        g.scatter = new MediumInteraction(p, -wi.d, wi.time, medium, mp.phase);
                        wo = new Ray(p, wi.d, wi.time);
                        return (bool)beta && (bool)r;
                    }
                    SampledSpectrum sigma_n = ClampZero(sigma_r - sigma_a - sigma_s);
                    Float pdfNull = T_maj[0] * sigma_n[0];
                    // (a null collision that only the rounding of the probabilities chose: nothing happened)
                    if (!(pdfNull > 0))
                    {
                        continue;
                    }
                    beta *= T_maj * sigma_n / pdfNull;
                    r *= T_maj * sigma_n / pdfNull;
                    if (!(bool)beta || !(bool)r)
                    {
                        weight = new SampledSpectrum(0f);
                        return false;
                    }
                }
            }
            weight = beta;
            m_ctx.r = r;
            return hit;
        }

        // Spectral tracking over the intervals (Kutz et al. 2017, section 5; see Volume.SpectralWalk). The events are the
        // scattering by each volume of the node, the null collision and, with "spectralabsorption", the absorption; with
        // "tracker" "decomposition" a collision is first the control component's with the probability mu_c / mu_bar, mu_c
        // being the sum over the volumes of their smallest density with their smallest coefficients over the wavelengths.
        private bool IntegrateSpectral(VolumeServices rs, Ray wi, bool hit, Float distance, out SampledSpectrum L, out SampledSpectrum weight, ref Point3f P,
                                       ref Ray wo, ref VolumeGeometry g)
        {
            VolumeIntegrator settings = medium.volumeIntegrator;
            bool decompose = settings.tracker is DecompositionTracker;
            L = new SampledSpectrum(0f);
            SampledSpectrum w = new SampledSpectrum(1f);
            Float rMean = m_ctx.r.Average();
            int count = aggregate.MemberCount();
            MediumProperties[] each = new MediumProperties[count];
            Float* scalarA = stackalloc Float[count];
            Float* scalarS = stackalloc Float[count];
            Float* measures = stackalloc Float[count];
            VolumeAggregate.Iterator interval = aggregate.GetIterator(wi.o, wi.d, distance);
            while (interval.Next())
            {
                int n = interval.NumVolumes();
                if (n == 0)
                {
                    continue;
                }
                Float majorant = Majorant(ref interval).MaxComponentValue();
                if (!(majorant > 0))
                {
                    continue;
                }
                int[] volumes = interval.Volumes();
                Float control = 0, controlAbsorption = 0;
                for (int k = 0; k < n; ++k)
                {
                    scalarA[k] = 0;
                    scalarS[k] = 0;
                }
                if (decompose && (bool)Control(ref interval))
                {
                    for (int k = 0; k < n; ++k)
                    {
                        scalarA[k] = controlA[k].MinComponentValue();
                        scalarS[k] = controlS[k].MinComponentValue();
                        control += scalarA[k] + scalarS[k];
                        controlAbsorption += scalarA[k];
                    }
                }
                Float pControl = control / majorant, residual = majorant - control;
                Float t = interval.Start(), tEnd = interval.End();
                while (true)
                {
                    t += -std.log(1 - rs.GenerateRandomNumber()) / majorant;
                    if (!(t < tEnd))
                    {
                        break;
                    }
                    Point3f p = wi.Apply(t);
                    int chosen = -1;
                    PhaseFunction phase = default;
                    if (pControl > 0 && rs.GenerateRandomNumber() < pControl)
                    {
                        // The control component: one volume by its share, an absorption or a scattering of weight one
                        Float uc = rs.GenerateRandomNumber() * control;
                        for (int k = 0; k < n; ++k)
                        {
                            Float share = scalarA[k] + scalarS[k];
                            if (share > 0)
                            {
                                chosen = k;
                                if (uc < share)
                                {
                                    break;
                                }
                                uc -= share;
                            }
                        }
                        if (rs.GenerateRandomNumber() * (scalarA[chosen] + scalarS[chosen]) < scalarA[chosen])
                        {
                            weight = new SampledSpectrum(0f);
                            return false;
                        }
                        phase = unit[volumes[chosen]].phase;
                    }
                    else
                    {
                        SampledSpectrum sigma_a, sigma_s, emission;
                        At(ref interval, p, wi.time, each, out sigma_a, out sigma_s, out emission);
                        if ((bool)emission)
                        {
                            L += m_ctx.emissionWeight * w * emission / (residual * rMean);
                        }
                        SampledSpectrum sigma_n = new SampledSpectrum(majorant) - sigma_a - sigma_s;
                        Float absorb = settings.spectralAbsorption ? SpectralMeasure(w, sigma_a - new SampledSpectrum(controlAbsorption)) : 0;
                        Float pass = SpectralMeasure(w, sigma_n), total = absorb + pass;
                        for (int k = 0; k < n; ++k)
                        {
                            each[k].sigma_s -= new SampledSpectrum(scalarS[k]);
                            measures[k] = SpectralMeasure(w, each[k].sigma_s);
                            total += measures[k];
                        }
                        if (!(total > 0))
                        {
                            weight = new SampledSpectrum(0f);
                            return false;
                        }
                        Float x = rs.GenerateRandomNumber() * total;
                        if (x < absorb)
                        {
                            weight = new SampledSpectrum(0f);
                            return false;
                        }
                        x -= absorb;
                        for (int k = 0; k < n && chosen < 0; ++k)
                        {
                            if (measures[k] > 0)
                            {
                                if (x < measures[k])
                                {
                                    chosen = k;
                                }
                                x -= measures[k];
                            }
                        }
                        if (chosen < 0)
                        {
                            // A null collision
                            if (!(pass > 0))
                            {
                                weight = new SampledSpectrum(0f);
                                return false;
                            }
                            w *= sigma_n * (total / (residual * pass));
                            if (!(bool)w)
                            {
                                weight = new SampledSpectrum(0f);
                                return false;
                            }
                            continue;
                        }
                        w *= each[chosen].sigma_s * (total / (residual * measures[chosen]));
                        phase = each[chosen].phase;
                    }
                    weight = w;
                    P = p;
                    g = new VolumeGeometry();
                    g.isSurface = false;
                    g.phase = phase;
                    g.scatter = new MediumInteraction(p, -wi.d, wi.time, medium, phase);
                    wo = new Ray(p, wi.d, wi.time);
                    return true;
                }
            }
            weight = w;
            return hit;
        }

        // The transmittance between two points, by ratio tracking over the same intervals (the course, listing 19)
        public override SampledSpectrum Transmittance(VolumeServices rs, Point3f P0, Point3f P1)
        {
            Float distance;
            Ray ray = Between(P0, P1, m_ctx.time, out distance);
            SampledSpectrum T_ray = new SampledSpectrum(1f), rl = new SampledSpectrum(1f), ru = new SampledSpectrum(1f);
            MediumProperties[] each = new MediumProperties[aggregate.MemberCount()];
            VolumeAggregate.Iterator interval = aggregate.GetIterator(ray.o, ray.d, distance);
            while (interval.Next())
            {
                if (interval.NumVolumes() == 0)
                {
                    continue;
                }
                SampledSpectrum sigma_maj = Majorant(ref interval);
                Float t = interval.Start(), tEnd = interval.End();
                while (true)
                {
                    Float step = sigma_maj[0] > 0 ? -std.log(1 - rs.GenerateRandomNumber()) / sigma_maj[0] : Infinity;
                    if (!(t + step < tEnd))
                    {
                        MediumEvents.FinishSegment(ref T_ray, ref rl, ref ru, Attenuation(sigma_maj, tEnd - t));
                        break;
                    }
                    t += step;
                    SampledSpectrum sigma_a, sigma_s, emission;
                    At(ref interval, ray.Apply(t), ray.time, each, out sigma_a, out sigma_s, out emission);
                    MediumEvents.RatioTrackingStep(ref T_ray, ref rl, ref ru, Attenuation(sigma_maj, step), sigma_maj, ClampZero(sigma_maj - sigma_a - sigma_s));
                    if (!(bool)T_ray)
                    {
                        return T_ray;
                    }
                }
            }
            m_ctx.r *= rl;
            return T_ray;
        }

        // What the volumes emit between two points (section 3.8), over the same intervals: at every collision of the
        // majorant, the transmittance so far times the summed sigma_a Le over the density of the collision
        public override SampledSpectrum Emission(VolumeServices rs, Point3f P0, Point3f P1)
        {
            Float distance;
            Ray ray = Between(P0, P1, m_ctx.time, out distance);
            // (the densities of the path that reaches P0 go on along the ray: EmissionOver)
            SampledSpectrum sum = new SampledSpectrum(0f), T = new SampledSpectrum(1f), re = m_ctx.emissionDensity;
            m_ctx.emissionDensityUsed = true;
            MediumProperties[] each = new MediumProperties[aggregate.MemberCount()];
            VolumeAggregate.Iterator interval = aggregate.GetIterator(ray.o, ray.d, distance);
            while (interval.Next())
            {
                if (interval.NumVolumes() == 0)
                {
                    continue;
                }
                SampledSpectrum sigma_maj = Majorant(ref interval);
                Float t = interval.Start(), tEnd = interval.End();
                while (true)
                {
                    Float step = sigma_maj[0] > 0 ? -std.log(1 - rs.GenerateRandomNumber()) / sigma_maj[0] : Infinity;
                    if (!(t + step < tEnd))
                    {
                        SampledSpectrum rest = Attenuation(sigma_maj, tEnd - t);
                        T *= rest / rest[0];
                        re *= rest / rest[0];
                        break;
                    }
                    t += step;
                    SampledSpectrum sigma_a, sigma_s, emission;
                    At(ref interval, ray.Apply(t), ray.time, each, out sigma_a, out sigma_s, out emission);
                    SampledSpectrum T_maj = Attenuation(sigma_maj, step);
                    Float pdf = sigma_maj[0] * T_maj[0];
                    SampledSpectrum density = re * T_maj * sigma_maj / pdf;
                    if ((bool)emission && (bool)density)
                    {
                        sum += T * T_maj / pdf * emission / density.Average();
                    }
                    T *= T_maj * ClampZero(sigma_maj - sigma_a - sigma_s) / pdf;
                    re *= T_maj * sigma_maj / pdf;
                    if (!(bool)T)
                    {
                        return sum;
                    }
                }
            }
            return sum;
        }

        private VolumeAggregate aggregate;
        private SampledSpectrum[] extinction;
        private bool[] known;
        // The coefficients at density 1 of the volumes that give them (unitState: 0 not asked, 1 given, -1 not given), and
        // the control component of each volume of the current interval
        private MediumProperties[] unit;
        private int[] unitState;
        private SampledSpectrum[] controlA, controlS;
    }
}
