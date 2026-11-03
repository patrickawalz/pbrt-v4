// (not in pbrt-v4) sdftexture.cs
//
// Texture "sdf": a signed distance field (see sdf.cs) turned into a value between 0 and 1 at every point, which is how an SDF
// becomes a volume. As the density of a "texture" medium it fills the solid with a medium (or a halo around it, or a thin shell),
// and the medium builds its majorants from RegionBounds(): over a box the distance cannot change by more than the Lipschitz bound
// times the half diagonal of the box, so the bounds are tight near the surface and zero or one far from it, and empty space costs
// nothing.
//
//   Texture "fractal" "float" "sdf" "string sdf" "mandelbulb(8, 10)" "string mode" "solid" "float blur" 0.02
//   MakeNamedMedium "cloud" "string type" "texture" "string density" "fractal" ...
//
// A field that is not rigorous (the Mandelbulb's distance estimate) gives the whole range 0 to 1 over every box, so the medium is
// correct but slow; "bool trustbounds" true uses the Lipschitz bound anyway (fast, and exact only where the estimate is good).
//
// Modes: "solid" is 1 inside the surface and 0 outside, with a smooth transition over the distance blur on either side of
// the surface; "exponential" is 1 inside and exp(-distance / falloff) outside (a glow); "shell" is 1 within thickness / 2 of the
// surface (a thin skin), with the transition blur; "distance" is the depth below the surface divided by depth, clamped to [0, 1]
// (dense in the core, fading toward the surface).

using System;

namespace pbrt
{

    // SdfDensityMode Definition
    public enum SdfDensityMode
    {
        Solid,
        Exponential,
        Shell,
        Distance
    }

    // SdfFloatTexture Definition
    public sealed unsafe class SdfFloatTexture : FloatTexture
    {
        // SdfFloatTexture Public Methods
        public SdfFloatTexture(Transform renderFromTexture, SdfNode field, SdfDensityMode mode, double blur, double falloff,
                               double thickness, double depth, bool trustBounds = false)
        {
            this.renderFromTexture = renderFromTexture;
            this.field = field;
            this.mode = mode;
            this.blur = Math.Max(blur, 0);
            this.falloff = Math.Max(falloff, 1e-30);
            this.thickness = thickness;
            this.depth = Math.Max(depth, 1e-30);
            lipschitz = field.Lipschitz();
            // The bounds over a box rely on the Lipschitz bound; a fractal distance estimate does not have a proven one
            useBounds = field.IsRigorous() || trustBounds;
        }

        // The value for a signed distance (a shell uses the absolute distance)
        public double Value(double distance)
        {
            switch (mode)
            {
                case SdfDensityMode.Solid:
                    return Transition(distance);
                case SdfDensityMode.Exponential:
                    return distance <= 0 ? 1 : Math.Exp(-distance / falloff);
                case SdfDensityMode.Shell:
                    return Transition(Math.Abs(distance) - 0.5 * thickness);
                default:
                    return Math.Min(Math.Max(-distance / depth, 0), 1);
            }
        }

        // 1 below -blur, 0 above blur, a smooth step between
        private double Transition(double distance)
        {
            if (blur == 0)
            {
                return distance <= 0 ? 1 : 0;
            }
            if (distance <= -blur)
            {
                return 1;
            }
            if (distance >= blur)
            {
                return 0;
            }
            double t = (blur - distance) / (2 * blur);
            return t * t * (3 - 2 * t);
        }

        public override Float Evaluate(TextureEvalContext ctx)
        {
            Point3f p = renderFromTexture.ApplyInverse(ctx.p);
            return (Float)Value(field.Evaluate(new SdfVector(p.x, p.y, p.z)));
        }

        public override Float MinValue()
        {
            return 0;
        }

        public override Float MaxValue()
        {
            return 1;
        }

        public override void RegionBounds(Bounds3f region, out Float lo, out Float hi)
        {
            if (!useBounds)
            {
                lo = MinValue();
                hi = MaxValue();
                return;
            }
            // The box of the region in texture space
            Bounds3f box = new Bounds3f();
            for (int i = 0; i < 8; ++i)
            {
                Point3f corner = renderFromTexture.ApplyInverse(region.Corner(i));
                box = i == 0 ? new Bounds3f(corner) : pbrt.Union(box, corner);
            }
            SdfVector center = new SdfVector(0.5 * ((double)box.pMin.x + box.pMax.x), 0.5 * ((double)box.pMin.y + box.pMax.y),
                                             0.5 * ((double)box.pMin.z + box.pMax.z));
            SdfVector half = new SdfVector(0.5 * ((double)box.pMax.x - box.pMin.x), 0.5 * ((double)box.pMax.y - box.pMin.y),
                                           0.5 * ((double)box.pMax.z - box.pMin.z));
            double reach = lipschitz * half.Length();
            double centerDistance = field.Evaluate(center);
            double nearest = centerDistance - reach, farthest = centerDistance + reach;
            double high, low;
            if (mode == SdfDensityMode.Shell)
            {
                double smallestAbsolute = nearest <= 0 && farthest >= 0 ? 0 : Math.Min(Math.Abs(nearest), Math.Abs(farthest));
                double largestAbsolute = Math.Max(Math.Abs(nearest), Math.Abs(farthest));
                high = Value(smallestAbsolute);
                low = Value(largestAbsolute);
            }
            else
            {
                // The value does not increase with the distance
                high = Value(nearest);
                low = Value(farthest);
            }
            // Round outward by one part in a million so that float rounding cannot put a value outside its bounds
            lo = (Float)Math.Max(low - 1e-6, 0);
            hi = (Float)Math.Min(high + 1e-6, 1);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ SdfFloatTexture mode: %d blur: %f falloff: %f thickness: %f depth: %f ]", (int)mode,
                                     (Float)blur, (Float)falloff, (Float)thickness, (Float)depth);
        }

        public static SdfFloatTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, FileLoc loc,
                                             Allocator alloc)
        {
            string expression = parameters.GetOneString("sdf", "");
            if (expression.Length == 0)
            {
                pbrt.ErrorExit(loc, "sdf texture: the \"string sdf\" expression is required.");
            }
            SdfParseContext context = new SdfParseContext();
            context.domainRadius = parameters.GetOneFloat("domainradius", 2);
            context.meshLoader = SdfMeshLoader.Load;
            SdfNode field = SdfParser.Parse(expression, context);
            string modeName = parameters.GetOneString("mode", "solid");
            SdfDensityMode mode;
            if (modeName == "solid")
            {
                mode = SdfDensityMode.Solid;
            }
            else if (modeName == "exponential")
            {
                mode = SdfDensityMode.Exponential;
            }
            else if (modeName == "shell")
            {
                mode = SdfDensityMode.Shell;
            }
            else if (modeName == "distance")
            {
                mode = SdfDensityMode.Distance;
            }
            else
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: unknown sdf texture \"mode\".", modeName));
                mode = SdfDensityMode.Solid;
            }
            return new SdfFloatTexture(renderFromTexture, field, mode, parameters.GetOneFloat("blur", 0),
                                       parameters.GetOneFloat("falloff", 0.1f), parameters.GetOneFloat("thickness", 0.1f),
                                       parameters.GetOneFloat("depth", 0.5f), parameters.GetOneBool("trustbounds", false));
        }

        private readonly Transform renderFromTexture;
        private readonly SdfNode field;
        private readonly SdfDensityMode mode;
        private readonly double blur, falloff, thickness, depth, lipschitz;
        private readonly bool useBounds;
    }

}  // namespace pbrt
