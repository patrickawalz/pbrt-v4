// (not in pbrt-v4) texturemedium.cs
//
// A medium whose density is a FloatTexture (procedural, image through a planar mapping, grid, or any combination of them).
// The texture only has to say how big and how small its values can be (MinValue() and MaxValue(), and RegionBounds() when it
// can do better over a box); the medium builds the majorant grid from those bounds, so no density grid is stored. The
// gradient of the density (FloatTexture.Gradient(), a finite difference unless the texture has an analytic one) is what bump
// and normal mapping from a volume take.

using System;
using System.Collections.Generic;

namespace pbrt
{

    // TextureMedium Definition
    public sealed unsafe class TextureMedium : Medium, IMajorantMedium<RayMajorantIterator>
    {
        // TextureMedium Public Methods
        public TextureMedium(Bounds3f bounds, Transform renderFromMedium, Spectrum sigma_a, Spectrum sigma_s, Float sigmaScale,
                             Float g, string densityTextureName, Float densityOffset, Float densityScale, int majorantRes,
                             int hierarchyRes, int hierarchyDepth, Float refineThreshold, Allocator alloc)
        {
            this.hierarchyRes = hierarchyRes;
            this.hierarchyDepth = hierarchyDepth;
            this.refineThreshold = refineThreshold;
            this.bounds = bounds;
            this.renderFromMedium = renderFromMedium;
            sigma_a_spec = new DenselySampledSpectrum(sigma_a, alloc);
            sigma_s_spec = new DenselySampledSpectrum(sigma_s, alloc);
            sigma_a_spec.Scale(sigmaScale);
            sigma_s_spec.Scale(sigmaScale);
            phase = new HGPhaseFunction(g);
            this.densityTextureName = densityTextureName;
            this.densityOffset = densityOffset;
            this.densityScale = densityScale;
            this.majorantRes = majorantRes;
        }

        public TextureMedium(Bounds3f bounds, Transform renderFromMedium, Spectrum sigma_a, Spectrum sigma_s, Float sigmaScale,
                             Float g, FloatTexture density, Float densityOffset, Float densityScale, int majorantRes,
                             int hierarchyRes, int hierarchyDepth, Float refineThreshold, Allocator alloc)
            : this(bounds, renderFromMedium, sigma_a, sigma_s, sigmaScale, g, "", densityOffset, densityScale, majorantRes,
                   hierarchyRes, hierarchyDepth, refineThreshold, alloc)
        {
            SetTexture(density);
        }

        // The named density texture exists only after the textures are created
        public void ResolveTextures(NamedTextures textures, FileLoc loc)
        {
            if (densityTextureName.Length == 0)
                return;
            if (!textures.floatTextures.TryGetValue(densityTextureName, out FloatTexture density))
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: float texture not defined (density of a \"texture\" medium)",
                                                      densityTextureName));
            SetTexture(density);
        }

        private void SetTexture(FloatTexture density)
        {
            texture = density;
            majorantGrid = new MajorantGrid(bounds, new Point3i(majorantRes, majorantRes, majorantRes), null);
            for (int z = 0; z < majorantRes; ++z)
            {
                for (int y = 0; y < majorantRes; ++y)
                {
                    for (int x = 0; x < majorantRes; ++x)
                        majorantGrid.Set(x, y, z, MaxDensity(majorantGrid.VoxelBounds(x, y, z)));
                }
            }
            topNodes = hierarchyDepth > 0 ? RefineMajorantGrid(majorantGrid, 1) : null;
        }

        // The volume aggregate: a voxel of a majorant grid whose densities are on average well below its majorant is replaced
        // by a finer grid (hierarchyRes^3) over the voxel, down to hierarchyDepth levels, with the bounds that the texture gives
        // for each sub-voxel. Voxels of zero density are skipped by the traversal and never refined.
        private MajorantNode[] RefineMajorantGrid(MajorantGrid grid, int depth)
        {
            MajorantNode[] nodes = new MajorantNode[grid.voxels.Length];
            if (depth > hierarchyDepth)
                return nodes;
            int n = hierarchyRes;
            for (int z = 0; z < grid.res.z; ++z)
            {
                for (int y = 0; y < grid.res.y; ++y)
                {
                    for (int x = 0; x < grid.res.x; ++x)
                    {
                        Float parent = grid.Lookup(x, y, z);
                        if (parent <= 0)
                            continue;
                        Bounds3f vb = grid.VoxelBounds(x, y, z);
                        Bounds3f region = new Bounds3f(grid.bounds.Lerp(vb.pMin), grid.bounds.Lerp(vb.pMax));
                        MajorantGrid child = new MajorantGrid(region, new Point3i(n, n, n), null);
                        Float sum = 0;
                        for (int cz = 0; cz < n; ++cz)
                        {
                            for (int cy = 0; cy < n; ++cy)
                            {
                                for (int cx = 0; cx < n; ++cx)
                                {
                                    Float childMax = std.min(parent, MaxDensity(region, child.VoxelBounds(cx, cy, cz)));
                                    child.Set(cx, cy, cz, childMax);
                                    sum += childMax;
                                }
                            }
                        }
                        if (sum / (n * n * n) < refineThreshold * parent)
                        {
                            nodes[x + grid.res.x * (y + grid.res.y * z)] =
                                new MajorantNode { sub = child, children = RefineMajorantGrid(child, depth + 1) };
                        }
                    }
                }
            }
            return nodes;
        }

        // The largest density in a box given in the coordinates of the bounds (0 to 1 over the bounds), from the bounds that the
        // texture gives for the box in render space
        public Float MaxDensity(Bounds3f unitBox)
        {
            return MaxDensity(bounds, unitBox);
        }

        // The same for a box given in the coordinates (0 to 1) of a region of the medium
        private Float MaxDensity(Bounds3f region, Bounds3f unitBox)
        {
            Point3f p0 = region.Lerp(unitBox.pMin), p1 = region.Lerp(unitBox.pMax);
            Bounds3f renderBox = renderFromMedium.Apply(new Bounds3f(p0, p1));
            texture.RegionBounds(renderBox, out Float lo, out Float hi);
            return std.max<Float>(0, std.max(densityOffset + densityScale * lo, densityOffset + densityScale * hi));
        }

        // The density at a point in render space; the medium is empty outside its bounds
        public Float Density(Point3f p)
        {
            if (!pbrt.Inside(renderFromMedium.ApplyInverse(p), bounds))
                return 0;
            TextureEvalContext ctx = new TextureEvalContext(p, default, default, default, default, 0, 0, 0, 0, 0);
            return std.max<Float>(0, densityOffset + densityScale * texture.Evaluate(ctx));
        }

        // The gradient of the density at a point in render space (zero where the density is clamped to zero)
        public Vector3f DensityGradient(Point3f p)
        {
            TextureEvalContext ctx = new TextureEvalContext(p, default, default, default, default, 0, 0, 0, 0, 0);
            if (densityOffset + densityScale * texture.Evaluate(ctx) <= 0)
                return new Vector3f(0, 0, 0);
            return densityScale * texture.Gradient(ctx);
        }

        public override bool IsEmissive() { return false; }

        public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda)
        {
            SampledSpectrum sigma_a = sigma_a_spec.Sample(lambda);
            SampledSpectrum sigma_s = sigma_s_spec.Sample(lambda);
            Float d = Density(p);
            sigma_a *= d;
            sigma_s *= d;
            return new MediumProperties { sigma_a = sigma_a, sigma_s = sigma_s, phase = phase, Le = new SampledSpectrum(0f) };
        }

        public RayMajorantIterator SampleRay(Ray ray, Float raytMax, SampledWavelengths lambda)
        {
            // Transform ray to medium's space and compute bounds overlap
            ray = renderFromMedium.ApplyInverse(ray, &raytMax);
            Float tMin, tMax;
            if (!bounds.IntersectP(ray.o, ray.d, raytMax, &tMin, &tMax))
                return new DDAMajorantIterator();
            SampledSpectrum sigma_t = sigma_a_spec.Sample(lambda) + sigma_s_spec.Sample(lambda);
            if (topNodes != null)
                return new HierarchicalMajorantIterator(ray, tMin, tMax, majorantGrid, topNodes, sigma_t);
            return new DDAMajorantIterator(ray, tMin, tMax, majorantGrid, sigma_t);
        }

        public override RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda, ScratchBuffer buf)
        {
            return SampleRay(ray, tMax, lambda);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ TextureMedium bounds: %s renderFromMedium: %s phase: %s texture: %s ]", bounds,
                                     renderFromMedium, phase, texture != null ? texture.ToString() : densityTextureName);
        }

        public static TextureMedium Create(ParameterDictionary parameters, Transform renderFromMedium, FileLoc loc,
                                           Allocator alloc)
        {
            string densityTextureName = parameters.GetOneString("density", "");
            if (densityTextureName.Length == 0)
                pbrt.ErrorExit(loc, "No \"string density\" (the name of a float texture) provided for texture medium.");
            Point3f p0 = parameters.GetOnePoint3f("p0", new Point3f(0f, 0f, 0f));
            Point3f p1 = parameters.GetOnePoint3f("p1", new Point3f(1f, 1f, 1f));
            Float g = parameters.GetOneFloat("g", 0f);
            Spectrum sigma_a = parameters.GetOneSpectrum("sigma_a", null, SpectrumType.Unbounded, alloc);
            if (sigma_a == null)
                sigma_a = new ConstantSpectrum(1f);
            Spectrum sigma_s = parameters.GetOneSpectrum("sigma_s", null, SpectrumType.Unbounded, alloc);
            if (sigma_s == null)
                sigma_s = new ConstantSpectrum(1f);
            Float sigmaScale = parameters.GetOneFloat("scale", 1f);
            Float densityOffset = parameters.GetOneFloat("densityoffset", 0f);
            Float densityScale = parameters.GetOneFloat("densityscale", 1f);
            int majorantRes = parameters.GetOneInt("majorantres", 16);
            int hierarchyRes = parameters.GetOneInt("hierarchyres", 4);
            int hierarchyDepth = parameters.GetOneInt("hierarchydepth", 0);
            Float refineThreshold = parameters.GetOneFloat("refinethreshold", 0.5f);
            return new TextureMedium(new Bounds3f(p0, p1), renderFromMedium, sigma_a, sigma_s, sigmaScale, g,
                                     densityTextureName, densityOffset, densityScale, majorantRes, hierarchyRes,
                                     hierarchyDepth, refineThreshold, alloc);
        }

        // TextureMedium Private Members
        private Bounds3f bounds;
        private Transform renderFromMedium;
        private DenselySampledSpectrum sigma_a_spec, sigma_s_spec;
        private HGPhaseFunction phase;
        private string densityTextureName;
        private FloatTexture texture;
        private Float densityOffset, densityScale;
        private int majorantRes;
        private MajorantGrid majorantGrid;
        private int hierarchyRes, hierarchyDepth;
        private Float refineThreshold;
        private MajorantNode[] topNodes;
    }

}  // namespace pbrt
