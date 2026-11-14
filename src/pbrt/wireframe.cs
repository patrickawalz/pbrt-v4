// (not in pbrt-v4) wireframe.cs
//
// The wireframe of a shape as a texture: 1 (or "line") on the lines and 0 (or "fill") between them.
//   - a triangle mesh: the edges of its triangles ("string edges" "all"), or without the longest edge of each triangle
//     ("quads", the default: two triangles that were a quadrilateral show the quadrilateral);
//   - a bilinear patch: its four edges;
//   - a shape without edges (sphere, cylinder, disk, curve, ...): the lines of its parameters, "float usteps" of them
//     over u in [0,1] and "float vsteps" over v (0: none in that direction). "string lines" "uv" draws these lines of the
//     texture coordinates on the meshes too, instead of their edges (a grid on a floor of two triangles).
// "float width" is the width of a line, in pixels ("string units" "pixels", the default: from the differentials of the ray,
// the same on the screen at any distance, and antialiased), in the units of the scene ("world") or in the coordinates of the
// face ("parameter").

using System;

namespace pbrt
{
    public enum WireframeUnits
    {
        Pixels,
        World,
        Parameter
    }

    public struct WireframeSettings
    {
        public Float width;
        public WireframeUnits units;
        public Float uSteps, vSteps;
        public bool quads;
        // The lines of the texture coordinates on every shape, the meshes too ("string lines" "uv")
        public bool uvLines;

        public static WireframeSettings Create(TextureParameterDictionary parameters, FileLoc loc)
        {
            WireframeSettings settings = new WireframeSettings();
            string units = parameters.GetOneString("units", "pixels");
            if (units == "pixels")
            {
                settings.units = WireframeUnits.Pixels;
            }
            else if (units == "world")
            {
                settings.units = WireframeUnits.World;
            }
            else if (units == "parameter")
            {
                settings.units = WireframeUnits.Parameter;
            }
            else
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("wireframe: unknown units \"%s\" (\"pixels\", \"world\", \"parameter\").", units));
            }
            settings.width = parameters.GetOneFloat("width", settings.units == WireframeUnits.Pixels ? 1.5f : 0.02f);
            settings.uSteps = parameters.GetOneFloat("usteps", 8f);
            settings.vSteps = parameters.GetOneFloat("vsteps", 8f);
            string edges = parameters.GetOneString("edges", "quads");
            if (edges != "quads" && edges != "all")
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("wireframe: unknown edges \"%s\" (\"quads\", \"all\").", edges));
            }
            settings.quads = edges == "quads";
            string lines = parameters.GetOneString("lines", "auto");
            if (lines != "auto" && lines != "uv")
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("wireframe: unknown lines \"%s\" (\"auto\", \"uv\").", lines));
            }
            settings.uvLines = lines == "uv";
            return settings;
        }
    }

    public static unsafe class Wireframe
    {
        // The differentials of the camera rays are those of a pixel times this (pbrt makes them smaller with more samples
        // in a pixel, for sharper texture filtering): the integrator sets it, and the distances in pixels are corrected by it
        public static Float differentialScale = 1;

        // How much of the point is line: 1 on a line, 0 away from it, between the two over the pixel that the edge of a line
        // crosses (with widths in pixels).
        public static Float Coverage(TextureEvalContext ctx, WireframeSettings settings)
        {
            Float distance = Distance(ctx, settings);
            Float half = settings.width / 2;
            if (settings.units == WireframeUnits.Pixels)
            {
                return pbrt.Clamp(half + 0.5f - distance, 0, 1);
            }
            return distance <= half ? 1 : 0;
        }

        // The distance to the nearest line in the units of the settings
        public static Float Distance(TextureEvalContext ctx, WireframeSettings settings)
        {
            SurfaceInteraction si = ctx.si;
            if (si == null || si.edgeKind == 0 || settings.uvLines)
            {
                return ParameterLines(ctx, settings);
            }
            // The coordinates (a, b) of the point on the face, p = p0 + a U + b V, and how they change over a pixel
            Float a = si.edgeCoordinates[0], b = si.edgeCoordinates[1];
            Vector3f U = si.edgeU, V = si.edgeV;
            Float uu = pbrt.Dot(U, U), uv = pbrt.Dot(U, V), vv = pbrt.Dot(V, V);
            Float determinant = uu * vv - uv * uv;
            if (determinant <= 0)
            {
                return pbrt.Infinity;
            }
            // d(a)/dp = (vv U - uv V) / determinant, d(b)/dp = (uu V - uv U) / determinant: their lengths are one over the
            // heights of the face above the lines a = 0 and b = 0
            Vector3f gradA = (vv * U - uv * V) / determinant, gradB = (uu * V - uv * U) / determinant;
            Float distance = pbrt.Infinity;
            if (si.edgeKind == 1)
            {
                // A triangle: the edges a = 0 (along V), b = 0 (along U) and a + b = 1 (along U - V)
                Float lengthA = vv, lengthB = uu, lengthC = uu + vv - 2 * uv;
                bool hideA = false, hideB = false, hideC = false;
                if (settings.quads)
                {
                    // (only an edge that is clearly the longest: an equilateral triangle keeps its three edges)
                    hideA = lengthA > 1.02f * lengthB && lengthA > 1.02f * lengthC;
                    hideB = lengthB > 1.02f * lengthA && lengthB > 1.02f * lengthC;
                    hideC = lengthC > 1.02f * lengthA && lengthC > 1.02f * lengthB;
                }
                if (!hideA)
                {
                    distance = std.min(distance, To(a, gradA, ctx, settings));
                }
                if (!hideB)
                {
                    distance = std.min(distance, To(b, gradB, ctx, settings));
                }
                if (!hideC)
                {
                    distance = std.min(distance, To(1 - a - b, -(gradA + gradB), ctx, settings));
                }
                return distance;
            }
            // A bilinear patch: the edges a = 0, a = 1, b = 0, b = 1
            distance = std.min(distance, To(std.min(a, 1 - a), gradA, ctx, settings));
            distance = std.min(distance, To(std.min(b, 1 - b), gradB, ctx, settings));
            return distance;
        }

        // The distance to the line where a coordinate of the face is zero, from the value of the coordinate at the point and
        // its derivative with respect to the point
        static Float To(Float value, Vector3f gradient, TextureEvalContext ctx, WireframeSettings settings)
        {
            value = std.max<Float>(0, value);
            switch (settings.units)
            {
            case WireframeUnits.Parameter:
                return value;
            case WireframeUnits.World:
            {
                Float rate = pbrt.Length(gradient);
                return rate > 0 ? value / rate : pbrt.Infinity;
            }
            default:
            {
                // (the change of the coordinate from a pixel to the next, along x and along y)
                Float dx = pbrt.Dot(gradient, ctx.dpdx), dy = pbrt.Dot(gradient, ctx.dpdy);
                Float rate = pbrt.SafeSqrt(dx * dx + dy * dy) / differentialScale;
                return rate > 0 ? value / rate : (value == 0 ? 0 : pbrt.Infinity);
            }
            }
        }

        // A shape without edges: the lines of its parameters
        static Float ParameterLines(TextureEvalContext ctx, WireframeSettings settings)
        {
            Float distance = pbrt.Infinity;
            if (settings.uSteps > 0)
            {
                Float scaled = ctx.uv[0] * settings.uSteps;
                Float du = std.abs(scaled - MathF.Round(scaled)) / settings.uSteps;
                distance = std.min(distance, ToLine(du, ctx.dudx, ctx.dudy, ctx.si != null ? pbrt.Length(ctx.si.dpdu) : 0, settings));
            }
            if (settings.vSteps > 0)
            {
                Float scaled = ctx.uv[1] * settings.vSteps;
                Float dv = std.abs(scaled - MathF.Round(scaled)) / settings.vSteps;
                distance = std.min(distance, ToLine(dv, ctx.dvdx, ctx.dvdy, ctx.si != null ? pbrt.Length(ctx.si.dpdv) : 0, settings));
            }
            return distance;
        }

        static Float ToLine(Float value, Float ddx, Float ddy, Float worldRate, WireframeSettings settings)
        {
            switch (settings.units)
            {
            case WireframeUnits.Parameter:
                return value;
            case WireframeUnits.World:
                return worldRate > 0 ? value * worldRate : pbrt.Infinity;
            default:
            {
                Float rate = pbrt.SafeSqrt(ddx * ddx + ddy * ddy) / differentialScale;
                return rate > 0 ? value / rate : (value == 0 ? 0 : pbrt.Infinity);
            }
            }
        }
    }

    public sealed unsafe class FloatWireframeTexture : FloatTexture
    {
        public FloatWireframeTexture(WireframeSettings settings, FloatTexture line, FloatTexture fill)
        {
            this.settings = settings;
            this.line = line;
            this.fill = fill;
        }

        public override Float MinValue()
        {
            return std.min(line.MinValue(), fill.MinValue());
        }

        public override Float MaxValue()
        {
            return std.max(line.MaxValue(), fill.MaxValue());
        }

        public override Float Evaluate(TextureEvalContext ctx)
        {
            Float w = Wireframe.Coverage(ctx, settings);
            Float onLine = 0, between = 0;
            if (w != 0)
            {
                onLine = line.Evaluate(ctx);
            }
            if (w != 1)
            {
                between = fill.Evaluate(ctx);
            }
            return w * onLine + (1 - w) * between;
        }

        public static FloatWireframeTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            return new FloatWireframeTexture(WireframeSettings.Create(parameters, loc), parameters.GetFloatTexture("line", 1f, alloc),
                                             parameters.GetFloatTexture("fill", 0f, alloc));
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ FloatWireframeTexture width: %f line: %s fill: %s ]", settings.width, line, fill);
        }

        private WireframeSettings settings;
        private FloatTexture line, fill;
    }

    public sealed unsafe class SpectrumWireframeTexture : SpectrumTexture
    {
        public SpectrumWireframeTexture(WireframeSettings settings, SpectrumTexture line, SpectrumTexture fill)
        {
            this.settings = settings;
            this.line = line;
            this.fill = fill;
        }

        public override Float MaxValue()
        {
            return std.max(line.MaxValue(), fill.MaxValue());
        }

        public override SampledSpectrum Evaluate(TextureEvalContext ctx, SampledWavelengths lambda)
        {
            Float w = Wireframe.Coverage(ctx, settings);
            SampledSpectrum onLine = new SampledSpectrum(0f), between = new SampledSpectrum(0f);
            if (w != 0)
            {
                onLine = line.Evaluate(ctx, lambda);
            }
            if (w != 1)
            {
                between = fill.Evaluate(ctx, lambda);
            }
            return w * onLine + (1 - w) * between;
        }

        public static SpectrumWireframeTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, SpectrumType spectrumType,
                                                      FileLoc loc, Allocator alloc)
        {
            SpectrumTexture line = parameters.GetSpectrumTexture("line", new ConstantSpectrum(0f), spectrumType, alloc);
            SpectrumTexture fill = parameters.GetSpectrumTexture("fill", new ConstantSpectrum(0.8f), spectrumType, alloc);
            return new SpectrumWireframeTexture(WireframeSettings.Create(parameters, loc), line, fill);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ SpectrumWireframeTexture width: %f line: %s fill: %s ]", settings.width, line, fill);
        }

        private WireframeSettings settings;
        private SpectrumTexture line, fill;
    }
}
