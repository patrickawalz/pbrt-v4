// (not in pbrt-v4) Open Shading Language integration: the "osl" material and the "osl" float and spectrum textures of the scene file.
//
//   Material "osl" "string shader" "matte" "rgb Kd" [ .8 .2 .2 ] "texture roughness" "rough"
//   Texture "marble" "spectrum" "osl" "string shader" "marble" "float scale" 4
//
// The parameter convention is documented in docs/osl-pbrt.md. This file turns the parameter list into a shader group description (OSLGroupSpec:
// layers, their parameter values, the connections between them), builds the ShaderGroup of the shading system from it, and implements the Material
// and Texture classes on top of OSLShading.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OIIO;
using OSL;

namespace pbrt
{
    public sealed class OSLParam
    {
        public string name;
        public TypeDesc type;
        public object value;
        public bool interpolated;
    }

    public sealed class OSLLayerSpec
    {
        public string shader;
        public string layer;
        public List<OSLParam> parameters = new List<OSLParam>();
    }

    public sealed class OSLConnectionSpec
    {
        public string srcLayer, srcParam, dstLayer, dstParam;
    }

    /// The description of a shader group: what ShaderGroupBegin/Parameter/Shader/ConnectShaders/ShaderGroupEnd are called with
    public sealed class OSLGroupSpec
    {
        public string groupSpec = null;
        public List<OSLLayerSpec> layers = new List<OSLLayerSpec>();
        public List<OSLConnectionSpec> connections = new List<OSLConnectionSpec>();
        public List<OSLTextureBinding> bindings = new List<OSLTextureBinding>();
        public string outputLayer = "";
        public string outputParam = "";
        public bool mayEmit = false;
        public string searchPath = "";
        private static int mergeCounter = 0;

        public OSLLayerSpec FindLayer(string name)
        {
            foreach (OSLLayerSpec l in layers)
            {
                if (l.layer == name)
                {
                    return l;
                }
            }
            return null;
        }

        /// Adds the layers and connections of another group in front of this one, with unique layer names; returns the new name of its output layer
        public string Merge(OSLGroupSpec other)
        {
            string prefix = "t" + System.Threading.Interlocked.Increment(ref mergeCounter) + "_";
            for (int i = 0; i < other.layers.Count; ++i)
            {
                OSLLayerSpec copy = new OSLLayerSpec();
                copy.shader = other.layers[i].shader;
                copy.layer = prefix + other.layers[i].layer;
                copy.parameters = new List<OSLParam>(other.layers[i].parameters);
                layers.Insert(i, copy);
            }
            foreach (OSLConnectionSpec c in other.connections)
            {
                OSLConnectionSpec cc = new OSLConnectionSpec();
                cc.srcLayer = prefix + c.srcLayer;
                cc.srcParam = c.srcParam;
                cc.dstLayer = prefix + c.dstLayer;
                cc.dstParam = c.dstParam;
                connections.Add(cc);
            }
            bindings.AddRange(other.bindings);
            mayEmit = mayEmit || other.mayEmit;
            return prefix + other.outputLayer;
        }
    }

    public static unsafe class OSLGroupBuilder
    {
        private static readonly string[] reserved = new string[] { "shader", "layers", "connect", "groupspec", "searchpath", "output", "displacement",
                                                                    "__shaderspace", "layername", "type" };

        private static bool IsReserved(string name, string[] extraReserved)
        {
            foreach (string r in reserved)
            {
                if (r == name)
                {
                    return true;
                }
            }
            if (extraReserved != null)
            {
                foreach (string r in extraReserved)
                {
                    if (r == name)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static string SceneDirectory(FileLoc loc)
        {
            if (loc != null && loc.filename != null && loc.filename.Length > 0)
            {
                try
                {
                    return Path.GetDirectoryName(Path.GetFullPath(loc.filename));
                }
                catch (Exception)
                {
                    return null;
                }
            }
            return null;
        }

        /// The value of a parameter of the scene file as the parameter of a shader; false if the type has no OSL equivalent
        public static bool ConvertParameter(ParsedParameter p, out OSLParam result)
        {
            result = new OSLParam();
            result.name = p.name;
            string t = p.type;
            if (t == "float")
            {
                result.type = p.floats.size() == 1 ? TypeDescs.TypeFloat : new TypeDesc(TypeDesc.FLOAT, (int)p.floats.size());
                float[] v = new float[p.floats.size()];
                for (int i = 0; i < v.Length; ++i)
                {
                    v[i] = p.floats[i];
                }
                result.value = v;
                return v.Length > 0;
            }
            if (t == "integer")
            {
                result.type = p.ints.size() == 1 ? TypeDescs.TypeInt : new TypeDesc(TypeDesc.INT, (int)p.ints.size());
                int[] v = new int[p.ints.size()];
                for (int i = 0; i < v.Length; ++i)
                {
                    v[i] = p.ints[i];
                }
                result.value = v;
                return v.Length > 0;
            }
            if (t == "bool")
            {
                result.type = TypeDescs.TypeInt;
                result.value = new int[] { p.bools.size() > 0 && p.bools[0] != 0 ? 1 : 0 };
                return p.bools.size() > 0;
            }
            if (t == "string")
            {
                result.type = p.strings.size() == 1 ? TypeDescs.TypeString : new TypeDesc(TypeDesc.STRING, (int)p.strings.size());
                string[] v = new string[p.strings.size()];
                for (int i = 0; i < v.Length; ++i)
                {
                    v[i] = p.strings[i];
                }
                result.value = v;
                return v.Length > 0;
            }
            TypeDesc vec = default(TypeDesc);
            bool isVec3 = true;
            if (t == "rgb")
            {
                vec = TypeDescs.TypeColor;
            }
            else if (t == "point3" || t == "point")
            {
                vec = TypeDescs.TypePoint;
            }
            else if (t == "vector3" || t == "vector")
            {
                vec = TypeDescs.TypeVector;
            }
            else if (t == "normal" || t == "normal3")
            {
                vec = TypeDescs.TypeNormal;
            }
            else
            {
                isVec3 = false;
            }
            if (isVec3)
            {
                if (p.floats.size() < 3 || p.floats.size() % 3 != 0)
                {
                    return false;
                }
                int n = (int)p.floats.size() / 3;
                result.type = n == 1 ? vec : new TypeDesc(vec.basetype, vec.aggregate, vec.vecsemantics, n);
                float[] v = new float[p.floats.size()];
                for (int i = 0; i < v.Length; ++i)
                {
                    v[i] = p.floats[i];
                }
                result.value = v;
                return true;
            }
            if (t == "point2" || t == "vector2")
            {
                if (p.floats.size() != 2)
                {
                    return false;
                }
                result.type = new TypeDesc(TypeDesc.FLOAT, 2);
                result.value = new float[] { p.floats[0], p.floats[1] };
                return true;
            }
            return false;
        }

        /// The description of a group from the parameters of a Material or a Texture
        public static OSLGroupSpec FromParameters(TextureParameterDictionary tparams, FileLoc loc, bool isTexture, string defaultOutput, Allocator alloc,
                                                  string[] extraReserved = null)
        {
            ParameterDictionary dict = tparams;
            List<ParsedParameter> plist = dict.GetParameterVector();
            OSLGroupSpec spec = new OSLGroupSpec();
            string sceneDir = SceneDirectory(loc);

            string groupspec = dict.GetOneString("groupspec", "");
            string shader = dict.GetOneString("shader", "");
            List<string> layerList = dict.GetStringArray("layers");
            List<string> connectList = dict.GetStringArray("connect");
            string extraPath = dict.GetOneString("searchpath", "");
            string outputName = dict.GetOneString("output", defaultOutput);
            if (extraPath.Length > 0)
            {
                OSLSystem.AddSearchPath(extraPath);
            }
            spec.searchPath = extraPath;

            if (groupspec.Length > 0)
            {
                spec.groupSpec = groupspec;
                spec.outputParam = outputName;
                foreach (ParsedParameter p in plist)
                {
                    p.lookedUp = true;
                }
                return spec;
            }

            if (shader.Length == 0 && (layerList == null || layerList.Count == 0))
            {
                ErrorExit(loc, "OSL material or texture needs a \"string shader\" (one layer), \"string layers\" or \"string groupspec\".");
            }
            if (shader.Length > 0)
            {
                OSLLayerSpec l = new OSLLayerSpec();
                l.shader = shader;
                l.layer = dict.GetOneString("layername", Path.GetFileNameWithoutExtension(shader));
                spec.layers.Add(l);
            }
            if (layerList != null)
            {
                foreach (string entry in layerList)
                {
                    OSLLayerSpec l = new OSLLayerSpec();
                    int colon = entry.IndexOf(':');
                    if (colon > 0)
                    {
                        l.layer = entry.Substring(0, colon).Trim();
                        l.shader = entry.Substring(colon + 1).Trim();
                    }
                    else
                    {
                        l.shader = entry.Trim();
                        l.layer = Path.GetFileNameWithoutExtension(l.shader);
                    }
                    spec.layers.Add(l);
                }
            }

            // Resolve the shaders: find or compile them
            foreach (OSLLayerSpec l in spec.layers)
            {
                string resolved, error;
                if (!OSLSystem.PrepareShader(l.shader, sceneDir, out resolved, out error))
                {
                    ErrorExit(loc, error);
                }
                l.shader = resolved;
                spec.mayEmit = spec.mayEmit || OSLSystem.ShaderMayEmit(resolved);
            }
            spec.outputLayer = spec.layers[spec.layers.Count - 1].layer;
            spec.outputParam = outputName;

            // Parameters: "layer.param" goes to that layer, an unqualified one to the last layer
            NamedTextures named = tparams.Textures();
            foreach (ParsedParameter p in plist)
            {
                if (IsReserved(p.name, extraReserved))
                {
                    p.lookedUp = true;
                    continue;
                }
                string layerName = spec.layers[spec.layers.Count - 1].layer;
                string paramName = p.name;
                int dot = p.name.IndexOf('.');
                if (dot > 0 && spec.FindLayer(p.name.Substring(0, dot)) != null)
                {
                    layerName = p.name.Substring(0, dot);
                    paramName = p.name.Substring(dot + 1);
                }
                OSLLayerSpec layer = spec.FindLayer(layerName);
                if (p.type == "texture")
                {
                    p.lookedUp = true;
                    if (p.strings.size() != 1)
                    {
                        ErrorExit(p.loc, StringPrintf("Parameter \"%s\" needs one texture name.", p.name));
                    }
                    string texName = p.strings[0];
                    FloatTexture ft;
                    SpectrumTexture st;
                    if (named.floatTextures.TryGetValue(texName, out ft))
                    {
                        OSLFloatTexture oft = ft as OSLFloatTexture;
                        if (oft != null)
                        {
                            ConnectTexture(spec, oft.Spec(), layer, paramName);
                        }
                        else
                        {
                            BindTexture(spec, layer, paramName, ft, null);
                        }
                    }
                    else if (named.unboundedSpectrumTextures.TryGetValue(texName, out st))
                    {
                        OSLSpectrumTexture ost = st as OSLSpectrumTexture;
                        if (ost != null)
                        {
                            ConnectTexture(spec, ost.Spec(), layer, paramName);
                        }
                        else
                        {
                            BindTexture(spec, layer, paramName, null, st);
                        }
                    }
                    else
                    {
                        ErrorExit(p.loc, StringPrintf("Couldn't find texture named \"%s\" for parameter \"%s\"", texName, p.name));
                    }
                    continue;
                }
                OSLParam op;
                p.lookedUp = true;
                if (!ConvertParameter(p, out op))
                {
                    Warning(p.loc, StringPrintf("OSL: parameter \"%s %s\" has a type that shaders can't take; ignored.", p.type, p.name));
                    continue;
                }
                op.name = paramName;
                layer.parameters.Add(op);
            }

            // Connections: "src.param dst.param"
            if (connectList != null)
            {
                foreach (string c in connectList)
                {
                    string[] parts = c.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length != 2)
                    {
                        ErrorExit(loc, StringPrintf("OSL \"connect\" entry \"%s\" must be \"srclayer.param dstlayer.param\".", c));
                    }
                    int d0 = parts[0].LastIndexOf('.'), d1 = parts[1].LastIndexOf('.');
                    if (d0 <= 0 || d1 <= 0)
                    {
                        ErrorExit(loc, StringPrintf("OSL \"connect\" entry \"%s\" must be \"srclayer.param dstlayer.param\".", c));
                    }
                    OSLConnectionSpec cs = new OSLConnectionSpec();
                    cs.srcLayer = parts[0].Substring(0, d0);
                    cs.srcParam = parts[0].Substring(d0 + 1);
                    cs.dstLayer = parts[1].Substring(0, d1);
                    cs.dstParam = parts[1].Substring(d1 + 1);
                    spec.connections.Add(cs);
                }
            }
            return spec;
        }

        private static void BindTexture(OSLGroupSpec spec, OSLLayerSpec layer, string paramName, FloatTexture ft, SpectrumTexture st)
        {
            OSLTextureBinding binding = new OSLTextureBinding();
            binding.parameter = paramName;
            binding.floatTexture = ft;
            binding.spectrumTexture = st;
            spec.bindings.Add(binding);
            OSLParam op = new OSLParam();
            op.name = paramName;
            op.interpolated = true;
            if (ft != null)
            {
                op.type = TypeDescs.TypeFloat;
                op.value = new float[] { 0 };
            }
            else
            {
                op.type = TypeDescs.TypeColor;
                op.value = new float[] { 0, 0, 0 };
            }
            layer.parameters.Add(op);
        }

        // An OSL texture connects as the layers of its own group in front of the shader
        private static void ConnectTexture(OSLGroupSpec spec, OSLGroupSpec texture, OSLLayerSpec layer, string paramName)
        {
            if (texture.groupSpec != null)
            {
                ErrorExit(null, "An OSL texture made with \"groupspec\" can't be connected to another shader; use layers.");
            }
            string outLayer = spec.Merge(texture);
            OSLConnectionSpec c = new OSLConnectionSpec();
            c.srcLayer = outLayer;
            c.srcParam = texture.outputParam;
            c.dstLayer = layer.layer;
            c.dstParam = paramName;
            spec.connections.Add(c);
        }

        private static int groupCounter = 0;

        /// Creates the ShaderGroup of a description and wraps it with what the integration needs to run it
        public static OSLShaderGroup Build(OSLGroupSpec spec, string kind, Transform worldFromShader, string[] outputs)
        {
            lock (buildMutex)
            {
                return BuildLocked(spec, kind, worldFromShader, outputs, false);
            }
        }

        /// derivatives: the output of the texture is to come with its derivatives (OSLShaderGroup.derivativeFloats says whether it does)
        public static OSLShaderGroup Build(OSLGroupSpec spec, string kind, Transform worldFromShader, string[] outputs, bool derivatives)
        {
            lock (buildMutex)
            {
                return BuildLocked(spec, kind, worldFromShader, outputs, derivatives);
            }
        }

        /// Asks OSL for the derivatives of the output of a texture: a renderer output that has a place in the output arena with "derivs" is
        /// differentiated by the optimizer (the shader is compiled with Dual2 values on whatever the output depends on), and the value and its
        /// two derivatives are copied to the arena when the layer ends. The type of the place has to be the one of the output, which is known
        /// from the shader (OSLQuery) and not from the group, whose symbols exist only once it is optimized.
        private static void RequestDerivatives(OSLShaderGroup g, OSLGroupSpec spec)
        {
            if (spec.groupSpec != null || spec.layers.Count == 0)
            {
                return;
            }
            OSLLayerSpec last = spec.layers[spec.layers.Count - 1];
            OSLQuery query = OSLSystem.Query(last.shader);
            if (query == null)
            {
                return;
            }
            string[] names = OSLTextureOutputs.Parameters(spec);
            foreach (string name in names)
            {
                OSLQuery.Parameter p = query.getparam(name);
                if (p == null)
                {
                    continue;
                }
                if (!p.isoutput || p.isclosure || p.isstruct || p.type.basetype != TypeDesc.FLOAT || p.type.arraylen != 0 ||
                    ((int)p.type.aggregate != 1 && (int)p.type.aggregate != 3))
                {
                    return;
                }
                List<SymLocationDesc> places = new List<SymLocationDesc>();
                places.Add(new SymLocationDesc(last.layer + "." + name, p.type, true, SymArena.Outputs, 0));
                g.group.add_symlocs(places);
                g.derivativeFloats = (int)p.type.aggregate;
                return;
            }
        }

        private static readonly object buildMutex = new object();

        private static OSLShaderGroup BuildLocked(OSLGroupSpec spec, string kind, Transform worldFromShader, string[] outputs, bool derivatives)
        {
            ShadingSystem ss = OSLSystem.ShadingSystem;
            OSLShaderGroup g = new OSLShaderGroup();
            g.name = kind + System.Threading.Interlocked.Increment(ref groupCounter);
            g.worldFromShader = worldFromShader;
            g.outputName = spec.outputParam;
            foreach (OSLTextureBinding b in spec.bindings)
            {
                g.bindings[new ustring(b.parameter).hash()] = b;
            }
            string usage = kind == "medium" ? "volume" : "surface";
            if (spec.groupSpec != null)
            {
                g.group = ss.ShaderGroupBegin(g.name, usage, spec.groupSpec);
            }
            else
            {
                g.group = ss.ShaderGroupBegin(g.name);
                foreach (OSLLayerSpec l in spec.layers)
                {
                    foreach (OSLParam p in l.parameters)
                    {
                        ss.Parameter(g.group, p.name, p.type, p.value, p.interpolated ? ParamHints.interpolated : ParamHints.none);
                    }
                    ss.Shader(g.group, usage, l.shader, l.layer);
                }
                foreach (OSLConnectionSpec c in spec.connections)
                {
                    if (!ss.ConnectShaders(g.group, c.srcLayer, c.srcParam, c.dstLayer, c.dstParam))
                    {
                        ErrorExit(null, StringPrintf("OSL: couldn't connect %s.%s to %s.%s", c.srcLayer, c.srcParam, c.dstLayer, c.dstParam));
                    }
                }
            }
            if (outputs != null && outputs.Length > 0)
            {
                ss.attribute(g.group, "renderer_outputs", new TypeDesc(TypeDesc.STRING, outputs.Length), outputs);
                if (derivatives)
                {
                    RequestDerivatives(g, spec);
                }
            }
            ss.ShaderGroupEnd(g.group);
            return g;
        }
    }

    /// Material "osl"
    public sealed unsafe class OSLMaterial : Material
    {
        private OSLShaderGroup group;
        private OSLGroupSpec spec;
        private FloatTexture displacement;
        private Image normalMap;

        public OSLMaterial(OSLGroupSpec spec, OSLShaderGroup group, FloatTexture displacement, Image normalMap)
        {
            this.spec = spec;
            this.group = group;
            this.displacement = displacement;
            this.normalMap = normalMap;
        }

        public bool MayEmit()
        {
            return spec.mayEmit;
        }

        public OSLShaderGroup Group()
        {
            return group;
        }

        public static OSLMaterial Create(TextureParameterDictionary parameters, Image normalMap, FileLoc loc, Allocator alloc)
        {
            OSLScene.Enabled = true;
            OSLGroupSpec spec = OSLGroupBuilder.FromParameters(parameters, loc, false, "", alloc);
            // shader space: the CTM at the Material directive (worldFromShader), injected by the scene builder
            Transform worldFromShader = new Transform();
            List<Float> m = ((ParameterDictionary)parameters).GetFloatArray("__shaderspace");
            if (m != null && m.Count == 16)
            {
                Float[] mat = m.ToArray();
                fixed (Float* pm = mat)
                {
                    worldFromShader = new Transform(new SquareMatrix4(pm));
                }
            }
            OSLShaderGroup group = OSLGroupBuilder.Build(spec, "material", worldFromShader, null);
            FloatTexture displacement = parameters.GetFloatTextureOrNull("displacement", alloc);
            return new OSLMaterial(spec, group, displacement, normalMap);
        }

        public override string ToString()
        {
            return StringPrintf("[ OSLMaterial group: %s ]", group.name);
        }

        public override FloatTexture GetDisplacement()
        {
            return displacement;
        }

        public override Image GetNormalMap()
        {
            return normalMap;
        }

        public override bool HasSubsurfaceScattering()
        {
            return false;
        }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda,
                                                           ScratchBuffer buf)
        {
            return null;
        }

        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval)
        {
            return true;
        }

        private static SurfaceInteraction PointOf(MaterialEvalContext ctx)
        {
            if (ctx.si != null)
            {
                return ctx.si;
            }
            // a context that has no interaction (a caller that only has the evaluation context): rebuild one
            Vector3f dpdv = Cross((Vector3f)ctx.ns, ctx.dpdus);
            SurfaceInteraction si = new SurfaceInteraction(new Point3fi(ctx.p), ctx.uv, ctx.wo, ctx.dpdus, dpdv, new Normal3f(0, 0, 0),
                                                           new Normal3f(0, 0, 0), 0, false);
            si.shading.n = ctx.ns;
            si.n = ctx.n;
            si.dpdx = ctx.dpdx;
            si.dpdy = ctx.dpdy;
            si.dudx = ctx.dudx;
            si.dudy = ctx.dudy;
            si.dvdx = ctx.dvdx;
            si.dvdy = ctx.dvdy;
            si.objectId = ctx.objectId;
            si.faceIndex = ctx.faceIndex;
            return si;
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda,
                                                       ScratchBuffer buf)
        {
            SurfaceInteraction si = PointOf(ctx);
            OSLShadeResult r = OSLShading.ShadeSurface(group, si, ctx.ns, ctx.dpdus, ref lambda);
            // A shader without lobes (an emitter) absorbs the path: a BSDF with no lobes
            OSLClosureBxDF bxdf = r.bxdf != null ? r.bxdf : new OSLClosureBxDF();
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(bxdf));
        }

        /// The emission at a point of a shape that is an area light (only the position, the normal and the uv are known there; the tangents are made up)
        public SampledSpectrum EmissionAt(Point3f p, Normal3f n, Point2f uv, Vector3f w, SampledWavelengths lambda, int objectId)
        {
            if (!spec.mayEmit)
            {
                return new SampledSpectrum(0f);
            }
            Vector3f nv = new Vector3f(n.x, n.y, n.z);
            Vector3f dpdu, dpdv;
            CoordinateSystem(nv, &dpdu, &dpdv);
            SurfaceInteraction si = new SurfaceInteraction(new Point3fi(p), uv, w, dpdu, dpdv, new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), 0, false);
            si.objectId = objectId;
            OSLShadeResult r = OSLShading.ShadeSurface(group, si, si.shading.n, si.shading.dpdu, ref lambda);
            return r.hasEmission ? r.emission : new SampledSpectrum(0f);
        }

        /// The radiance that the surface emits towards the viewer at a point (emission() and uniform_edf() closures of the shader)
        public SampledSpectrum Emission(SurfaceInteraction si, SampledWavelengths lambda)
        {
            if (!spec.mayEmit)
            {
                return new SampledSpectrum(0f);
            }
            OSLShadeResult r = OSLShading.ShadeSurface(group, si, si.shading.n, si.shading.dpdu, ref lambda);
            return r.hasEmission ? r.emission : new SampledSpectrum(0f);
        }
    }

    /// Texture "name" "float" "osl"
    public sealed unsafe class OSLFloatTexture : FloatTexture
    {
        private OSLGroupSpec spec;
        private OSLShaderGroup group;

        public OSLFloatTexture(OSLGroupSpec spec, OSLShaderGroup group)
        {
            this.spec = spec;
            this.group = group;
        }

        public OSLGroupSpec Spec()
        {
            return spec;
        }

        public static OSLFloatTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            OSLScene.Enabled = true;
            OSLGroupSpec spec = OSLGroupBuilder.FromParameters(parameters, loc, true, "Fout", alloc);
            OSLShaderGroup group = OSLGroupBuilder.Build(spec, "floattexture", new Transform(), OSLTextureOutputs.Names(spec));
            group.renderFromShader = renderFromTexture;
            group.hasRenderFromShader = true;
            OSLTextureOutputs.Resolve(group, spec);
            return new OSLFloatTexture(spec, group);
        }

        public override Float Evaluate(TextureEvalContext ctx)
        {
            float v;
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            if (!OSLShading.EvalOutput(group, ctx.si, ctx, ref lambda, &v, 1))
            {
                return 0;
            }
            return v;
        }

        // The group that OSL differentiates is a second one, built when a derivative is first asked for: a texture that is only ever
        // evaluated keeps the shader without derivatives.
        private OSLShaderGroup derivativeGroup;
        private bool derivativeGroupBuilt = false;
        private readonly object derivativeMutex = new object();

        private OSLShaderGroup DerivativeGroup()
        {
            if (!derivativeGroupBuilt)
            {
                lock (derivativeMutex)
                {
                    if (!derivativeGroupBuilt)
                    {
                        derivativeGroup = OSLTextureOutputs.Differentiated(spec, "floattexture", group);
                        derivativeGroupBuilt = true;
                    }
                }
            }
            return derivativeGroup;
        }

        public override bool HasAnalyticDerivatives()
        {
            return DerivativeGroup() != null;
        }

        protected override Float ValueAndDerivatives(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            OSLShaderGroup d = DerivativeGroup();
            float v, du, dv;
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            if (d == null || !OSLShading.EvalOutput(d, ctx.si, ctx, ref lambda, directions, &v, &du, &dv, 1))
            {
                Float value = Evaluate(ctx);
                FiniteDifferences(ctx, value, directions, dfdu, dfdv);
                return value;
            }
            if (dfdu != null)
            {
                *dfdu = du;
            }
            if (dfdv != null)
            {
                *dfdv = dv;
            }
            return v;
        }

        public override Float MinValue()
        {
            return 0;
        }

        public override Float MaxValue()
        {
            return 1;
        }

        public override string ToString()
        {
            return StringPrintf("[ OSLFloatTexture group: %s ]", group.name);
        }
    }

    /// Texture "name" "spectrum" "osl": the color output of a shader as an albedo, an unbounded spectrum or an illuminant, by the spectrum type
    public sealed unsafe class OSLSpectrumTexture : SpectrumTexture
    {
        private OSLGroupSpec spec;
        private OSLShaderGroup group;
        private SpectrumType spectrumType;

        public OSLSpectrumTexture(OSLGroupSpec spec, OSLShaderGroup group, SpectrumType spectrumType)
        {
            this.spec = spec;
            this.group = group;
            this.spectrumType = spectrumType;
        }

        public OSLGroupSpec Spec()
        {
            return spec;
        }

        public static OSLSpectrumTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, SpectrumType spectrumType,
                                                FileLoc loc, Allocator alloc)
        {
            OSLScene.Enabled = true;
            OSLGroupSpec spec = OSLGroupBuilder.FromParameters(parameters, loc, true, "Cout", alloc);
            OSLShaderGroup group = OSLGroupBuilder.Build(spec, "spectrumtexture", new Transform(), OSLTextureOutputs.Names(spec));
            group.renderFromShader = renderFromTexture;
            group.hasRenderFromShader = true;
            OSLTextureOutputs.Resolve(group, spec);
            return new OSLSpectrumTexture(spec, group, spectrumType);
        }

        private OSLShaderGroup derivativeGroup;
        private bool derivativeGroupBuilt = false;
        private readonly object derivativeMutex = new object();

        private OSLShaderGroup DerivativeGroup()
        {
            if (!derivativeGroupBuilt)
            {
                lock (derivativeMutex)
                {
                    if (!derivativeGroupBuilt)
                    {
                        derivativeGroup = OSLTextureOutputs.Differentiated(spec, "spectrumtexture", group);
                        derivativeGroupBuilt = true;
                    }
                }
            }
            return derivativeGroup;
        }

        public override bool HasAnalyticDerivatives()
        {
            return DerivativeGroup() != null;
        }

        private SampledSpectrum Convert(float* v, SampledWavelengths lambda)
        {
            switch (spectrumType)
            {
                case SpectrumType.Albedo:
                    return OSLSystem.ToSpectrum(Clamp(v[0], 0f, 1f), Clamp(v[1], 0f, 1f), Clamp(v[2], 0f, 1f), lambda);
                case SpectrumType.Illuminant:
                    return OSLSystem.ToEmissionSpectrum(v[0], v[1], v[2], lambda);
                default:
                    return OSLSystem.ToSpectrum(v[0], v[1], v[2], lambda);
            }
        }

        private SampledSpectrum ConvertDerivative(float* v, float* d, SampledWavelengths lambda)
        {
            switch (spectrumType)
            {
                case SpectrumType.Albedo:
                    // A channel held at 1 does not move
                    return OSLSystem.ToSpectrumDerivative(Clamp(v[0], 0f, 1f), Clamp(v[1], 0f, 1f), Clamp(v[2], 0f, 1f), v[0] < 1 ? d[0] : 0,
                                                          v[1] < 1 ? d[1] : 0, v[2] < 1 ? d[2] : 0, lambda);
                case SpectrumType.Illuminant:
                    return OSLSystem.ToEmissionSpectrumDerivative(v[0], v[1], v[2], d[0], d[1], d[2], lambda);
                default:
                    return OSLSystem.ToSpectrumDerivative(v[0], v[1], v[2], d[0], d[1], d[2], lambda);
            }
        }

        // The derivatives of the color are OSL's; the spectrum follows through the conversion
        protected override SampledSpectrum ValueAndDerivatives(TextureEvalContext ctx, SampledWavelengths lambda, TextureDirections directions,
                                                               SampledSpectrum* dfdu, SampledSpectrum* dfdv)
        {
            OSLShaderGroup d = DerivativeGroup();
            float* v = stackalloc float[3];
            float* du = stackalloc float[3];
            float* dv = stackalloc float[3];
            if (d == null || !OSLShading.EvalOutput(d, ctx.si, ctx, ref lambda, directions, v, du, dv, 3))
            {
                SampledSpectrum value = Evaluate(ctx, lambda);
                FiniteDifferences(ctx, lambda, value, directions, dfdu, dfdv);
                return value;
            }
            if (dfdu != null)
            {
                *dfdu = ConvertDerivative(v, du, lambda);
            }
            if (dfdv != null)
            {
                *dfdv = ConvertDerivative(v, dv, lambda);
            }
            return Convert(v, lambda);
        }

        public override SampledSpectrum Evaluate(TextureEvalContext ctx, SampledWavelengths lambda)
        {
            float* v = stackalloc float[3];
            if (!OSLShading.EvalOutput(group, ctx.si, ctx, ref lambda, v, 3))
            {
                return new SampledSpectrum(0f);
            }
            switch (spectrumType)
            {
                case SpectrumType.Albedo:
                    return OSLSystem.ToSpectrum(Clamp(v[0], 0f, 1f), Clamp(v[1], 0f, 1f), Clamp(v[2], 0f, 1f), lambda);
                case SpectrumType.Illuminant:
                    return OSLSystem.ToEmissionSpectrum(v[0], v[1], v[2], lambda);
                default:
                    return OSLSystem.ToSpectrum(v[0], v[1], v[2], lambda);
            }
        }

        public override Float MaxValue()
        {
            return spectrumType == SpectrumType.Albedo ? 1 : 100;
        }

        public override string ToString()
        {
            return StringPrintf("[ OSLSpectrumTexture group: %s ]", group.name);
        }
    }

    /// The output variable of a texture group: the name that the scene gave, or the usual names of the outputs of texture shaders
    public static class OSLTextureOutputs
    {
        private static readonly string[] fallbacks = new string[] { "Cout", "Fout", "Color", "Fac", "Out", "out", "result" };

        /// The names of the output without the layer, in the order in which they are looked for
        public static string[] Parameters(OSLGroupSpec spec)
        {
            List<string> names = new List<string>();
            names.Add(spec.outputParam);
            foreach (string f in fallbacks)
            {
                if (f != spec.outputParam)
                {
                    names.Add(f);
                }
            }
            return names.ToArray();
        }

        /// The group of a texture that also gives the derivatives of its output; null if OSL cannot give them (a group given as one string,
        /// an output that is not one float or three)
        public static OSLShaderGroup Differentiated(OSLGroupSpec spec, string kind, OSLShaderGroup group)
        {
            OSLShaderGroup d = OSLGroupBuilder.Build(spec, kind, new Transform(), Names(spec), true);
            if (d.derivativeFloats == 0)
            {
                return null;
            }
            d.renderFromShader = group.renderFromShader;
            d.hasRenderFromShader = group.hasRenderFromShader;
            Resolve(d, spec);
            return d;
        }

        public static string[] Names(OSLGroupSpec spec)
        {
            List<string> names = new List<string>();
            string layer = spec.layers.Count > 0 ? spec.layers[spec.layers.Count - 1].layer + "." : "";
            names.Add(layer + spec.outputParam);
            foreach (string f in fallbacks)
            {
                if (f != spec.outputParam)
                {
                    names.Add(layer + f);
                }
            }
            return names.ToArray();
        }

        public static void Resolve(OSLShaderGroup group, OSLGroupSpec spec)
        {
            group.outputName = spec.outputParam;
            group.outputCandidates = Names(spec);
        }
    }
}
