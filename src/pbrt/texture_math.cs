// (not in pbrt-v4) Float math textures and the cloud texture of LuxCore, with the interval bounds a majorant grid needs.
//
// Every texture gives MinValue()/MaxValue() from its inputs' global bounds and RegionBounds() from their region bounds,
// by interval arithmetic, so a medium driven by a combination of textures keeps a rigorous majorant.

using System;
using System.Collections.Generic;

namespace pbrt
{
    // Interval helpers shared by the math textures
    public static unsafe partial class pbrt
    {
        // The interval of a quotient; unbounded when the divisor interval contains zero
        public static void DivideInterval(Float lo1, Float hi1, Float lo2, Float hi2, out Float lo, out Float hi)
        {
            if (lo2 <= 0 && hi2 >= 0)
            {
                // The divisor can be zero (where the texture returns 0) or arbitrarily close to it
                lo = -Infinity;
                hi = Infinity;
                return;
            }
            Float a = lo1 / lo2, b = lo1 / hi2, c = hi1 / lo2, d = hi1 / hi2;
            lo = std.min(std.min(a, b), std.min(c, d));
            hi = std.max(std.max(a, b), std.max(c, d));
        }

        // pow() of LuxCore's PowerTexture: zero for a negative base with a non-integer exponent
        public static Float SafePow(Float baseValue, Float exponent)
        {
            if (baseValue < 0 && exponent != (int)exponent)
                return 0;
            return (Float)Math.Pow(baseValue, exponent);
        }

        // Bounds of SafePow over base in [lo1,hi1] and exponent in [lo2,hi2]. For a non-negative base the power is
        // monotone in each argument separately, so the extremes are at the corners; a base that can be negative is
        // bounded by its magnitude.
        public static void PowerInterval(Float lo1, Float hi1, Float lo2, Float hi2, out Float lo, out Float hi)
        {
            Float bMax = std.max(std.abs(lo1), std.abs(hi1));
            Float bMin = (lo1 <= 0 && hi1 >= 0) ? 0 : std.min(std.abs(lo1), std.abs(hi1));
            Float a = (Float)Math.Pow(bMin, lo2), b = (Float)Math.Pow(bMin, hi2);
            Float c = (Float)Math.Pow(bMax, lo2), d = (Float)Math.Pow(bMax, hi2);
            Float magnitude = std.max(std.max(a, b), std.max(c, d));
            if (float.IsNaN((float)magnitude))
                magnitude = Infinity;
            lo = (lo1 >= 0) ? std.min(std.min(a, b), std.min(c, d)) : -magnitude;
            hi = magnitude;
            if (lo1 >= 0 && !(lo <= hi))
            {
                lo = -Infinity;
                hi = Infinity;
            }
        }
    }

    // FloatAddTexture Definition
    public sealed unsafe class FloatAddTexture : FloatTexture
    {
        public FloatAddTexture(FloatTexture tex1, FloatTexture tex2) { this.tex1 = tex1; this.tex2 = tex2; }

        public override Float MinValue() { return tex1.MinValue() + tex2.MinValue(); }
        public override Float MaxValue() { return tex1.MaxValue() + tex2.MaxValue(); }

        public override void RegionBounds(Bounds3f region, out Float lo, out Float hi)
        {
            tex1.RegionBounds(region, out Float lo1, out Float hi1);
            tex2.RegionBounds(region, out Float lo2, out Float hi2);
            lo = lo1 + lo2;
            hi = hi1 + hi2;
        }

        public override Float Evaluate(TextureEvalContext ctx) { return tex1.Evaluate(ctx) + tex2.Evaluate(ctx); }

        public override string ToString() { return pbrt.StringPrintf("[ FloatAddTexture tex1: %s tex2: %s ]", tex1, tex2); }

        public static FloatAddTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, FileLoc loc,
                                             Allocator alloc)
        {
            return new FloatAddTexture(parameters.GetFloatTexture("texture1", 1f, alloc),
                                       parameters.GetFloatTexture("texture2", 1f, alloc));
        }

        private FloatTexture tex1, tex2;
    }

    // FloatSubtractTexture Definition
    public sealed unsafe class FloatSubtractTexture : FloatTexture
    {
        public FloatSubtractTexture(FloatTexture tex1, FloatTexture tex2) { this.tex1 = tex1; this.tex2 = tex2; }

        public override Float MinValue() { return tex1.MinValue() - tex2.MaxValue(); }
        public override Float MaxValue() { return tex1.MaxValue() - tex2.MinValue(); }

        public override void RegionBounds(Bounds3f region, out Float lo, out Float hi)
        {
            tex1.RegionBounds(region, out Float lo1, out Float hi1);
            tex2.RegionBounds(region, out Float lo2, out Float hi2);
            lo = lo1 - hi2;
            hi = hi1 - lo2;
        }

        public override Float Evaluate(TextureEvalContext ctx) { return tex1.Evaluate(ctx) - tex2.Evaluate(ctx); }

        public override string ToString() { return pbrt.StringPrintf("[ FloatSubtractTexture tex1: %s tex2: %s ]", tex1, tex2); }

        public static FloatSubtractTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                                  FileLoc loc, Allocator alloc)
        {
            return new FloatSubtractTexture(parameters.GetFloatTexture("texture1", 1f, alloc),
                                            parameters.GetFloatTexture("texture2", 1f, alloc));
        }

        private FloatTexture tex1, tex2;
    }

    // FloatAbsTexture Definition
    public sealed unsafe class FloatAbsTexture : FloatTexture
    {
        public FloatAbsTexture(FloatTexture tex) { this.tex = tex; }

        private static void Bounds(Float lo, Float hi, out Float absLo, out Float absHi)
        {
            if (lo >= 0)
            {
                absLo = lo;
                absHi = hi;
            }
            else if (hi <= 0)
            {
                absLo = -hi;
                absHi = -lo;
            }
            else
            {
                absLo = 0;
                absHi = std.max(-lo, hi);
            }
        }

        public override Float MinValue() { Bounds(tex.MinValue(), tex.MaxValue(), out Float lo, out Float hi); return lo; }
        public override Float MaxValue() { Bounds(tex.MinValue(), tex.MaxValue(), out Float lo, out Float hi); return hi; }

        public override void RegionBounds(Bounds3f region, out Float lo, out Float hi)
        {
            tex.RegionBounds(region, out Float tlo, out Float thi);
            Bounds(tlo, thi, out lo, out hi);
        }

        public override Float Evaluate(TextureEvalContext ctx) { return std.abs(tex.Evaluate(ctx)); }

        public override string ToString() { return pbrt.StringPrintf("[ FloatAbsTexture tex: %s ]", tex); }

        public static FloatAbsTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, FileLoc loc,
                                             Allocator alloc)
        {
            return new FloatAbsTexture(parameters.GetFloatTexture("texture", 1f, alloc));
        }

        private FloatTexture tex;
    }

    // FloatClampTexture Definition
    public sealed unsafe class FloatClampTexture : FloatTexture
    {
        public FloatClampTexture(FloatTexture tex, Float minValue, Float maxValue)
        {
            this.tex = tex;
            this.minValue = minValue;
            this.maxValue = maxValue;
        }

        public override Float MinValue() { return pbrt.Clamp(tex.MinValue(), minValue, maxValue); }
        public override Float MaxValue() { return pbrt.Clamp(tex.MaxValue(), minValue, maxValue); }

        public override void RegionBounds(Bounds3f region, out Float lo, out Float hi)
        {
            tex.RegionBounds(region, out Float tlo, out Float thi);
            lo = pbrt.Clamp(tlo, minValue, maxValue);
            hi = pbrt.Clamp(thi, minValue, maxValue);
        }

        public override Float Evaluate(TextureEvalContext ctx) { return pbrt.Clamp(tex.Evaluate(ctx), minValue, maxValue); }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ FloatClampTexture tex: %s min: %f max: %f ]", tex, minValue, maxValue);
        }

        // LuxCore defaults both limits to 0, which clamps everything to 0; the default upper limit here is 1
        public static FloatClampTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, FileLoc loc,
                                               Allocator alloc)
        {
            return new FloatClampTexture(parameters.GetFloatTexture("texture", 1f, alloc), parameters.GetOneFloat("min", 0f),
                                         parameters.GetOneFloat("max", 1f));
        }

        private FloatTexture tex;
        private Float minValue, maxValue;
    }

    // FloatDivideTexture Definition
    public sealed unsafe class FloatDivideTexture : FloatTexture
    {
        public FloatDivideTexture(FloatTexture tex1, FloatTexture tex2) { this.tex1 = tex1; this.tex2 = tex2; }

        public override Float MinValue()
        {
            pbrt.DivideInterval(tex1.MinValue(), tex1.MaxValue(), tex2.MinValue(), tex2.MaxValue(), out Float lo, out Float hi);
            return lo;
        }

        public override Float MaxValue()
        {
            pbrt.DivideInterval(tex1.MinValue(), tex1.MaxValue(), tex2.MinValue(), tex2.MaxValue(), out Float lo, out Float hi);
            return hi;
        }

        public override void RegionBounds(Bounds3f region, out Float lo, out Float hi)
        {
            tex1.RegionBounds(region, out Float lo1, out Float hi1);
            tex2.RegionBounds(region, out Float lo2, out Float hi2);
            pbrt.DivideInterval(lo1, hi1, lo2, hi2, out lo, out hi);
        }

        // As in LuxCore, a zero divisor gives 0
        public override Float Evaluate(TextureEvalContext ctx)
        {
            Float value2 = tex2.Evaluate(ctx);
            if (value2 == 0)
                return 0;
            return tex1.Evaluate(ctx) / value2;
        }

        public override string ToString() { return pbrt.StringPrintf("[ FloatDivideTexture tex1: %s tex2: %s ]", tex1, tex2); }

        public static FloatDivideTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, FileLoc loc,
                                                Allocator alloc)
        {
            return new FloatDivideTexture(parameters.GetFloatTexture("texture1", 1f, alloc),
                                          parameters.GetFloatTexture("texture2", 1f, alloc));
        }

        private FloatTexture tex1, tex2;
    }

    // FloatPowerTexture Definition
    public sealed unsafe class FloatPowerTexture : FloatTexture
    {
        public FloatPowerTexture(FloatTexture baseTexture, FloatTexture exponent)
        {
            this.baseTexture = baseTexture;
            this.exponent = exponent;
        }

        public override Float MinValue()
        {
            pbrt.PowerInterval(baseTexture.MinValue(), baseTexture.MaxValue(), exponent.MinValue(), exponent.MaxValue(), out Float lo, out Float hi);
            return lo;
        }

        public override Float MaxValue()
        {
            pbrt.PowerInterval(baseTexture.MinValue(), baseTexture.MaxValue(), exponent.MinValue(), exponent.MaxValue(), out Float lo, out Float hi);
            return hi;
        }

        public override void RegionBounds(Bounds3f region, out Float lo, out Float hi)
        {
            baseTexture.RegionBounds(region, out Float lo1, out Float hi1);
            exponent.RegionBounds(region, out Float lo2, out Float hi2);
            pbrt.PowerInterval(lo1, hi1, lo2, hi2, out lo, out hi);
        }

        public override Float Evaluate(TextureEvalContext ctx)
        {
            return pbrt.SafePow(baseTexture.Evaluate(ctx), exponent.Evaluate(ctx));
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ FloatPowerTexture base: %s exponent: %s ]", baseTexture, exponent);
        }

        public static FloatPowerTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, FileLoc loc,
                                               Allocator alloc)
        {
            return new FloatPowerTexture(parameters.GetFloatTexture("base", 1f, alloc),
                                         parameters.GetFloatTexture("exponent", 1f, alloc));
        }

        private FloatTexture baseTexture, exponent;
    }

    // FloatRemapTexture Definition
    // Maps the value from [sourcemin, sourcemax] to [targetmin, targetmax] and clamps it to the target range
    public sealed unsafe class FloatRemapTexture : FloatTexture
    {
        public FloatRemapTexture(FloatTexture value, FloatTexture sourceMin, FloatTexture sourceMax, FloatTexture targetMin,
                                 FloatTexture targetMax)
        {
            this.value = value;
            this.sourceMin = sourceMin;
            this.sourceMax = sourceMax;
            this.targetMin = targetMin;
            this.targetMax = targetMax;
        }

        public static Float ClampedRemap(Float v, Float sMin, Float sMax, Float tMin, Float tMax)
        {
            Float lo = std.min(sMin, sMax), hi = std.max(sMin, sMax);
            v = pbrt.Clamp(v, lo, hi);
            Float result = (sMax == sMin) ? tMin : tMin + (v - sMin) / (sMax - sMin) * (tMax - tMin);
            return pbrt.Clamp(result, std.min(tMin, tMax), std.max(tMin, tMax));
        }

        // The result is a clamped blend of the target limits, so it lies between their extremes
        public override Float MinValue() { return std.min(targetMin.MinValue(), targetMax.MinValue()); }
        public override Float MaxValue() { return std.max(targetMin.MaxValue(), targetMax.MaxValue()); }

        public override void RegionBounds(Bounds3f region, out Float lo, out Float hi)
        {
            targetMin.RegionBounds(region, out Float lo1, out Float hi1);
            targetMax.RegionBounds(region, out Float lo2, out Float hi2);
            lo = std.min(lo1, lo2);
            hi = std.max(hi1, hi2);
        }

        public override Float Evaluate(TextureEvalContext ctx)
        {
            return ClampedRemap(value.Evaluate(ctx), sourceMin.Evaluate(ctx), sourceMax.Evaluate(ctx), targetMin.Evaluate(ctx),
                                targetMax.Evaluate(ctx));
        }

        public override string ToString() { return pbrt.StringPrintf("[ FloatRemapTexture value: %s ]", value); }

        public static FloatRemapTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, FileLoc loc,
                                               Allocator alloc)
        {
            return new FloatRemapTexture(parameters.GetFloatTexture("value", 0.5f, alloc),
                                         parameters.GetFloatTexture("sourcemin", 0f, alloc),
                                         parameters.GetFloatTexture("sourcemax", 1f, alloc),
                                         parameters.GetFloatTexture("targetmin", 0f, alloc),
                                         parameters.GetFloatTexture("targetmax", 1f, alloc));
        }

        private FloatTexture value, sourceMin, sourceMax, targetMin, targetMax;
    }

    // FloatRoundingTexture Definition
    // Rounds the value to the nearest multiple of the increment
    public sealed unsafe class FloatRoundingTexture : FloatTexture
    {
        public FloatRoundingTexture(FloatTexture tex, FloatTexture increment) { this.tex = tex; this.increment = increment; }

        public static Float Round(Float value, Float increment)
        {
            if (value == increment || increment == 0)
                return value;
            Float innerBound = increment * (int)(value / increment);
            Float outerBound = (value > 0) ? innerBound + increment : innerBound - increment;
            return (std.abs(innerBound - value) < std.abs(outerBound - value)) ? innerBound : outerBound;
        }

        // The rounded value is within one increment of the value
        private Float Slack(Float incrementLo, Float incrementHi) { return std.max(std.abs(incrementLo), std.abs(incrementHi)); }

        public override Float MinValue() { return tex.MinValue() - Slack(increment.MinValue(), increment.MaxValue()); }
        public override Float MaxValue() { return tex.MaxValue() + Slack(increment.MinValue(), increment.MaxValue()); }

        public override void RegionBounds(Bounds3f region, out Float lo, out Float hi)
        {
            tex.RegionBounds(region, out Float tlo, out Float thi);
            increment.RegionBounds(region, out Float ilo, out Float ihi);
            lo = tlo - Slack(ilo, ihi);
            hi = thi + Slack(ilo, ihi);
        }

        public override Float Evaluate(TextureEvalContext ctx) { return Round(tex.Evaluate(ctx), increment.Evaluate(ctx)); }

        public override string ToString() { return pbrt.StringPrintf("[ FloatRoundingTexture tex: %s increment: %s ]", tex, increment); }

        public static FloatRoundingTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                                  FileLoc loc, Allocator alloc)
        {
            return new FloatRoundingTexture(parameters.GetFloatTexture("texture", 1f, alloc),
                                            parameters.GetFloatTexture("increment", 0.5f, alloc));
        }

        private FloatTexture tex, increment;
    }

    // FloatModuloTexture Definition
    public sealed unsafe class FloatModuloTexture : FloatTexture
    {
        public FloatModuloTexture(FloatTexture tex, FloatTexture modulo) { this.tex = tex; this.modulo = modulo; }

        // fmod keeps the sign of the value and is smaller than both the value and the modulus in magnitude
        private static void Bounds(Float lo1, Float hi1, Float lo2, Float hi2, out Float lo, out Float hi)
        {
            Float m = std.max(std.abs(lo2), std.abs(hi2));
            lo = (lo1 >= 0) ? 0 : std.max(lo1, -m);
            hi = (hi1 <= 0) ? 0 : std.min(hi1, m);
        }

        public override Float MinValue() { Bounds(tex.MinValue(), tex.MaxValue(), modulo.MinValue(), modulo.MaxValue(), out Float lo, out Float hi); return lo; }
        public override Float MaxValue() { Bounds(tex.MinValue(), tex.MaxValue(), modulo.MinValue(), modulo.MaxValue(), out Float lo, out Float hi); return hi; }

        public override void RegionBounds(Bounds3f region, out Float lo, out Float hi)
        {
            tex.RegionBounds(region, out Float lo1, out Float hi1);
            modulo.RegionBounds(region, out Float lo2, out Float hi2);
            Bounds(lo1, hi1, lo2, hi2, out lo, out hi);
        }

        // A zero modulus gives 0, as in LuxCore
        public override Float Evaluate(TextureEvalContext ctx)
        {
            Float modulus = modulo.Evaluate(ctx);
            if (modulus == 0)
                return 0;
            return (Float)(tex.Evaluate(ctx) % modulus);
        }

        public override string ToString() { return pbrt.StringPrintf("[ FloatModuloTexture tex: %s modulo: %s ]", tex, modulo); }

        public static FloatModuloTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, FileLoc loc,
                                                Allocator alloc)
        {
            return new FloatModuloTexture(parameters.GetFloatTexture("texture", 1f, alloc),
                                          parameters.GetFloatTexture("modulo", 0.5f, alloc));
        }

        private FloatTexture tex, modulo;
    }

    // FloatCompareTexture Definition
    // 1 where texture1 is greater than (or less than) texture2, else 0
    public sealed unsafe class FloatCompareTexture : FloatTexture
    {
        public FloatCompareTexture(FloatTexture tex1, FloatTexture tex2, bool greater)
        {
            this.tex1 = tex1;
            this.tex2 = tex2;
            this.greater = greater;
        }

        private void Bounds(Float lo1, Float hi1, Float lo2, Float hi2, out Float lo, out Float hi)
        {
            // Always true, always false, or either
            bool alwaysTrue = greater ? lo1 > hi2 : hi1 < lo2;
            bool alwaysFalse = greater ? hi1 <= lo2 : lo1 >= hi2;
            lo = alwaysTrue ? 1 : 0;
            hi = alwaysFalse ? 0 : 1;
        }

        public override Float MinValue() { Bounds(tex1.MinValue(), tex1.MaxValue(), tex2.MinValue(), tex2.MaxValue(), out Float lo, out Float hi); return lo; }
        public override Float MaxValue() { Bounds(tex1.MinValue(), tex1.MaxValue(), tex2.MinValue(), tex2.MaxValue(), out Float lo, out Float hi); return hi; }

        public override void RegionBounds(Bounds3f region, out Float lo, out Float hi)
        {
            tex1.RegionBounds(region, out Float lo1, out Float hi1);
            tex2.RegionBounds(region, out Float lo2, out Float hi2);
            Bounds(lo1, hi1, lo2, hi2, out lo, out hi);
        }

        public override Float Evaluate(TextureEvalContext ctx)
        {
            Float a = tex1.Evaluate(ctx), b = tex2.Evaluate(ctx);
            return (greater ? a > b : a < b) ? 1f : 0f;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ FloatCompareTexture tex1: %s tex2: %s greater: %s ]", tex1, tex2, greater);
        }

        public static FloatCompareTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                                 bool greater, FileLoc loc, Allocator alloc)
        {
            return new FloatCompareTexture(parameters.GetFloatTexture("texture1", 1f, alloc),
                                           parameters.GetFloatTexture("texture2", 1f, alloc), greater);
        }

        private FloatTexture tex1, tex2;
        private bool greater;
    }

    // FloatCloudTexture Definition
    // LuxCore's "cloud": a hemisphere of density (or a set of cumulus spheres) in the unit cube of the texture's 3D
    // mapping, perturbed by turbulence. Its value is in [0,1]. Note that LuxCore's scene parser passes the properties to
    // the constructor in a different order than the constructor declares them, so its "variability", "baseflatness" and
    // "spheres" do not reach the parameters of those names; this port gives each property its own meaning.
    public sealed unsafe class FloatCloudTexture : FloatTexture
    {
        public FloatCloudTexture(TextureMapping3D mapping, Float radius, Float noiseScale, Float turbulence, Float sharpness,
                                 Float variability, Float baseFlatness, int octaves, Float omega, Float noiseOffset,
                                 int numSpheres, Float sphereSize)
        {
            this.mapping = mapping;
            this.radius = radius;
            this.firstNoiseScale = noiseScale;
            this.turbulenceAmount = turbulence;
            this.sharpness = sharpness;
            this.variability = variability;
            this.baseFlatness = baseFlatness;
            this.numOctaves = octaves;
            this.omega = omega;
            this.noiseOffset = noiseOffset;
            this.numSpheres = numSpheres;
            this.sphereSize = sphereSize;
            baseFadeDistance = 1 - baseFlatness;
            sphereCentre = new Point3f(.5f, .5f, 1f / 3f);
            if (numSpheres > 0)
            {
                // Spheres on the edge of the hemisphere
                RNG rng = new RNG((ulong)(noiseOffset * 1000003) + 1);
                spheres = new Point3f[numSpheres];
                sphereRadii = new Float[numSpheres];
                for (int i = 0; i < numSpheres; ++i)
                {
                    sphereRadii[i] = (CloudRand(ref rng, 10) / 2f + 0.5f) * sphereSize;
                    Vector3f onEdge = new Vector3f(radius / 2f * CloudRand(ref rng, 1000), 0, 0);
                    Float angleY = -180f * CloudRand(ref rng, 1000);
                    Float angleZ = 360f * CloudRand(ref rng, 1000);
                    onEdge = pbrt.RotateZ(angleZ).Apply(pbrt.RotateY(angleY).Apply(onEdge));
                    Point3f finalPosition = sphereCentre + onEdge;
                    finalPosition += CloudTurbulence(finalPosition + new Vector3f(noiseOffset * 4f, 0, 0), radius * 0.7f, 2) * radius * 1.5f;
                    spheres[i] = finalPosition;
                }
            }
        }

        private static Float CloudRand(ref RNG rng, int resolution)
        {
            return (Float)(rng.Uniform<uint>() % (uint)resolution) / resolution;
        }

        public override Float MinValue() { return 0; }
        public override Float MaxValue() { return 1; }

        private Float CloudNoise(Point3f p, Float omegaValue, int octaves)
        {
            Float sum = 0, lambda = 1, o = 1;
            for (int i = 0; i < octaves; ++i)
            {
                sum += o * pbrt.Noise(new Point3f(lambda * p.x, lambda * p.y, lambda * p.z));
                lambda *= 1.99f;
                o *= omegaValue;
            }
            return sum;
        }

        private Float NoiseMask(Point3f p)
        {
            return CloudNoise(new Point3f(p.x / radius * 1.4f, p.y / radius * 1.4f, p.z / radius * 1.4f), omega, 1);
        }

        private Vector3f CloudTurbulence(Point3f p, Float noiseScale, int octaves)
        {
            Point3f c0 = new Point3f(p.x / noiseScale, p.y / noiseScale, p.z / noiseScale);
            Point3f c1 = c0 + new Vector3f(noiseOffset, noiseOffset, noiseOffset);
            Point3f c2 = c1 + new Vector3f(noiseOffset, noiseOffset, noiseOffset);

            Float noiseAmount = 1;
            if (variability < 1)
                noiseAmount = (1 - variability) * 1 + variability * NoiseMask(p + new Vector3f(noiseOffset * 4f, 0, 0));
            noiseAmount = pbrt.Clamp(noiseAmount, 0, 1);

            Vector3f turbulence = new Vector3f(CloudNoise(c0, omega, octaves) - 0.15f, CloudNoise(c1, omega, octaves) - 0.15f,
                                               -CloudNoise(c2, omega, octaves));
            if (p.z < sphereCentre.z + baseFadeDistance)
                turbulence.z *= (p.z - sphereCentre.z) / (2f * baseFadeDistance);
            return turbulence * noiseAmount;
        }

        private Float CloudShape(Point3f p)
        {
            if (numSpheres > 0)
            {
                for (int i = 0; i < numSpheres; ++i)
                    if (pbrt.Length(p - spheres[i]) < sphereRadii[i])
                        return 1;
                return 0;
            }
            Vector3f fromCentre = p - sphereCentre;
            Float amount = 1 - pbrt.Length(fromCentre) / radius;
            if (amount < 0)
                return 0;
            // The base below the cloud's height fades out
            if (p.z < sphereCentre.z)
            {
                if (p.z < sphereCentre.z - radius * 0.4f)
                    return 0;
                amount *= 1 - (Float)Math.Cos((fromCentre.z + baseFadeDistance) / baseFadeDistance * Math.PI * 0.5);
            }
            return std.max(amount, 0);
        }

        public override Float Evaluate(TextureEvalContext ctx)
        {
            Point3f p = mapping.Map(ctx).p;
            Point3f q = p + turbulenceAmount * CloudTurbulence(p, firstNoiseScale, numOctaves);
            Float amount = CloudShape(q);
            Float finalValue = (Float)Math.Pow(amount * Math.Pow(10, .7), sharpness);
            return std.min(finalValue, 1);
        }

        public override string ToString() { return pbrt.StringPrintf("[ FloatCloudTexture radius: %f ]", radius); }

        public static FloatCloudTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, FileLoc loc,
                                               Allocator alloc)
        {
            TextureMapping3D mapping = TextureMapping3D.Create(parameters, renderFromTexture, loc, alloc);
            return new FloatCloudTexture(mapping, parameters.GetOneFloat("radius", .5f), parameters.GetOneFloat("noisescale", .5f),
                                         parameters.GetOneFloat("turbulence", .01f), parameters.GetOneFloat("sharpness", 6f),
                                         parameters.GetOneFloat("variability", .9f), parameters.GetOneFloat("baseflatness", .8f),
                                         parameters.GetOneInt("octaves", 1), parameters.GetOneFloat("omega", .5f),
                                         parameters.GetOneFloat("noiseoffset", 0f), parameters.GetOneInt("spheres", 0),
                                         parameters.GetOneFloat("spheresize", .15f));
        }

        private TextureMapping3D mapping;
        private Float radius, firstNoiseScale, turbulenceAmount, sharpness, variability, baseFlatness, omega, noiseOffset;
        private Float sphereSize, baseFadeDistance;
        private int numOctaves, numSpheres;
        private Point3f sphereCentre;
        private Point3f[] spheres;
        private Float[] sphereRadii;
    }
}
