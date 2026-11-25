// (not in pbrt-v4) lightlayers.cs
//
// Light layers: the image kept as one buffer for each group of lights, so that the groups can be rebalanced while the render
// runs and after it, as the light groups of LuxRender are.
//
//     the scene        A light, an area light or an emissive medium is put in a layer with "string layer" "name"; pbrt's
//                      Attribute "light" "string layer" "name" does it for all the lights that follow. One with no layer is in
//                      the layer "default", number 0.
//     the integrators  Li() still returns one spectrum. Where an integrator adds emitted light it also says whose it was,
//                      LightLayers.Add(light, L), which keeps the part of each layer but the default in a scratch of the thread.
//                      Film::AddSample() takes the scratch with the sample; what was not said to be of a layer is the default's.
//                      A splat is one path with one light and carries its layer, Film::AddSplat(p, L, lambda, layer).
//     the film         A pixel holds its sums once for each layer. Film::GetImage(metadata, splatScale, layers) is the image
//                      with each layer times what its settings make of it: enabled, a scale, a tint and a colour temperature.

using System;
using System.Collections.Generic;

namespace pbrt
{
    using static pbrt;

    // LightLayer Definition: the settings of a layer. They are read when an image is made of the film, and may change at any time.
    public sealed class LightLayer
    {
        public LightLayer(string name)
        {
            this.name = name;
        }

        public readonly string name;
        public bool enabled = true;
        public Float scale = 1;
        public RGB tint = new RGB(1, 1, 1);
        // (in kelvin; 0 is none: the layer is multiplied by the colour of a black body of this temperature, at the luminance 1)
        public Float temperature = 0;
    }

    // LightLayerSplit Definition: the layers of a path that ends at more than one light at once, a ray that leaves a scene of
    // several lights at infinity, and the share of the light of the path that each has, wavelength by wavelength. The path has
    // one weight for the sum of what those lights give, so the shares of the sum are the shares of the path.
    public sealed class LightLayerSplit
    {
        public int[] layers;
        public SampledSpectrum[] shares;
    }

    // LightLayers Definition
    public sealed class LightLayers
    {
        // The layers of the scene that is being read and rendered
        public static readonly LightLayers Scene = new LightLayers();

        // LightLayers Public Methods
        public LightLayers()
        {
            Reset();
        }

        // Back to the one layer every scene has
        public void Reset()
        {
            lock (sync)
            {
                layers = new LightLayer[] { new LightLayer("default") };
            }
        }

        public int Count
        {
            get { return layers.Length; }
        }

        public LightLayer this[int index]
        {
            get { return layers[index]; }
        }

        // The number of the layer of a name, which is a new layer if there is none of that name
        public int Index(string name)
        {
            lock (sync)
            {
                for (int i = 0; i < layers.Length; ++i)
                {
                    if (layers[i].name == name)
                    {
                        return i;
                    }
                }
                LightLayer[] grown = new LightLayer[layers.Length + 1];
                Array.Copy(layers, grown, layers.Length);
                grown[layers.Length] = new LightLayer(name);
                layers = grown;
                return grown.Length - 1;
            }
        }

        // What the output RGB of a layer is multiplied by: nothing left of a layer that is not enabled, and otherwise its scale,
        // its tint and the colour of its temperature
        public RGB Factor(int index, RGBColorSpace colorSpace)
        {
            LightLayer layer = layers[index];
            if (!layer.enabled)
            {
                return new RGB(0, 0, 0);
            }
            RGB factor = layer.scale * layer.tint;
            if (layer.temperature > 0)
            {
                XYZ xyz = SpectrumToXYZ(new BlackbodySpectrum(layer.temperature));
                RGB colour = colorSpace.ToRGB(new XYZ(xyz.X / xyz.Y, 1, xyz.Z / xyz.Y));
                factor = new RGB(factor.r * Math.Max(0, colour.r), factor.g * Math.Max(0, colour.g), factor.b * Math.Max(0, colour.b));
            }
            return factor;
        }

        // The scratch of the calling thread: what the sample that is being taken has of each layer but the default
        //
        // Add: light of a layer has been added to the sample
        public static void Add(int layer, SampledSpectrum L)
        {
            if (layer == 0)
            {
                return;
            }
            if (scratch == null || scratch.Length <= layer)
            {
                SampledSpectrum[] grown = new SampledSpectrum[Math.Max(layer + 1, Scene.Count)];
                if (scratch != null)
                {
                    Array.Copy(scratch, grown, scratch.Length);
                }
                scratch = grown;
            }
            scratch[layer] += L;
            scratchUsed = true;
        }

        public static void Add(Light light, SampledSpectrum L)
        {
            if (light.layer != 0)
            {
                Add(light.layer, L);
            }
        }

        // The layer of the light that the calling thread sampled last, for the callers of a function that samples a light and
        // returns what it gives: Sampled(light) in the function, AddSampled(L) where its return value is added
        public static void Sampled(Light light)
        {
            sampledLayer = light.layer;
            sampledSplit = null;
        }

        // (the light of a path that ends at more than one light, of more than one layer)
        public static void Sampled(LightLayerSplit split)
        {
            sampledLayer = 0;
            sampledSplit = split;
        }

        public static LightLayerSplit SampledSplit()
        {
            return sampledSplit;
        }

        public static void Sampled(int layer)
        {
            sampledLayer = layer;
            sampledSplit = null;
        }

        public static int SampledLayer()
        {
            return sampledLayer;
        }

        public static void AddSampled(SampledSpectrum L)
        {
            if (sampledSplit != null)
            {
                for (int i = 0; i < sampledSplit.layers.Length; ++i)
                {
                    Add(sampledSplit.layers[i], L * sampledSplit.shares[i]);
                }
            }
            else if (sampledLayer != 0)
            {
                Add(sampledLayer, L);
            }
        }

        // The sample has been multiplied by a factor
        public static void Scale(SampledSpectrum factor)
        {
            if (scratchUsed)
            {
                for (int i = 1; i < scratch.Length; ++i)
                {
                    scratch[i] *= factor;
                }
            }
        }

        // The sample is done with (or thrown away)
        public static void Clear()
        {
            if (scratchUsed)
            {
                for (int i = 1; i < scratch.Length; ++i)
                {
                    scratch[i] = new SampledSpectrum(0f);
                }
                scratchUsed = false;
            }
        }

        // What the sample has of the layers, for the film: null if all of it is the default's
        public static SampledSpectrum[] Scratch()
        {
            return scratchUsed ? scratch : null;
        }

        // LightLayers Private Members
        private readonly object sync = new object();
        private volatile LightLayer[] layers;
        [ThreadStatic] private static SampledSpectrum[] scratch;
        [ThreadStatic] private static bool scratchUsed;
        [ThreadStatic] private static int sampledLayer;
        [ThreadStatic] private static LightLayerSplit sampledSplit;
    }
}
