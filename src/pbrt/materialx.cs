// (not in pbrt-v4) materialx.cs
//
// Material "materialx" "string filename" "look.mtlx" [ "string material" "name" ]: a material read from a MaterialX document
// (https://materialx.org, the specification of the Academy Software Foundation). This is a reader of the part of MaterialX
// that describes surfaces, mapped on the lobes of this branch, with an evaluator of its pattern nodes. It is not the MaterialX
// library: there is no code generation and no standard library of node definitions.
//
// What is read:
//   - the material: a <surfacematerial> (the one named, or the first), or without one the first surface shader;
//   - the surface shaders open_pbr_surface, standard_surface, gltf_pbr and UsdPreviewSurface, as an OpenPBRMaterial (openpbr.cs)
//     with their inputs under the names of OpenPBR (MaterialXReader.Tables). The options of that material that MaterialX has
//     no input for may be given beside "filename": "specular_lobe", "metal_lobe", "coat_lobe", the distributions, "scatterdepth",
//     "maxdepth", "nsamples", "subsurface_mode", "volume_scattering";
//   - a <surface> node of the physically based shading library with the BSDF nodes under it, as a MaterialXMaterial:
//       oren_nayar_diffuse_bsdf   EonDiffuseBxDF                 burley_diffuse_bsdf   OSLBurleyDiffuseBxDF
//       translucent_bsdf          DiffuseTransmissionBxDF        subsurface_bsdf       a walked medium (as OpenPBR's)
//       dielectric_bsdf           DielectricBxDF (scatter_mode)  conductor_bsdf        ConductorBxDF (ior, extinction)
//       generalized_schlick_bsdf  GeneralizedSchlickBxDF         sheen_bsdf            SGGXSheenBxDF
//       thin_film_bsdf            the Airy sum on the dielectric or the conductor that it is layered on
//       mix, add, multiply        MixBxDF, TintedBxDF
//       layer                     a StackBxDF where the top transmits (a dielectric, a sheen): the light between the two is
//                                 followed, where MaterialX scales the base by one minus the albedo of the top. A
//                                 generalized Schlick on top is the dielectric with its reflectance along the normal.
//     and the opacity of the surface;
//       chiang_hair_bsdf          HairBxDF (ior, absorption_coefficient, roughness_R, cuticle_angle)
//   - inputs: values, and graphs of the pattern nodes of materialx_patterns.cs (images, coordinates, arithmetic, channels,
//     colour, noise, conditionals), in a <nodegraph> or not, with the node definitions of the document (<nodedef> and the
//     <nodegraph> that implements it, interface names) and the colour spaces srgb_texture, g22_rec709 and lin_rec709.
// What is not: the pattern nodes that are not listed there (an input that leads to one keeps its default, with a warning),
// the definitions of the MaterialX standard libraries themselves (a node is known here by its name, not through stdlib),
// the other colour spaces and units, EDFs (in pbrt-v4 a surface emits as an area light of its shape, not through its
// material), VDFs, displacement and light shaders, the tints of the hair BSDF, geometry assignments (<look>,
// <materialassign>: the material is assigned to shapes by the pbrt scene), and code generation.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace pbrt
{
    public sealed class MtlxInput
    {
        public Float[] value;
        public string text;
        public FloatTexture floatTexture;
        public SpectrumTexture spectrumTexture;
        public MtlxExpr expr;

        public bool IsConstant() { return expr == null; }
    }

    public sealed class MtlxNode
    {
        public string category, name;
        public Dictionary<string, MtlxInput> inputs = new Dictionary<string, MtlxInput>();
        public Dictionary<string, MtlxNode> links = new Dictionary<string, MtlxNode>();
    }

    public sealed unsafe class MaterialXReader
    {
        private XElement root;
        private string directory;
        private FileLoc loc;
        private Allocator alloc;
        public NamedTextures textures = new NamedTextures();
        private int textureCount = 0;

        public MaterialXReader(string filename, FileLoc loc, Allocator alloc)
        {
            this.loc = loc;
            this.alloc = alloc;
            if (!File.Exists(filename))
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: MaterialX document not found", filename));
            }
            directory = Path.GetDirectoryName(Path.GetFullPath(filename));
            root = XDocument.Load(filename).Root;
            if (root == null || root.Name.LocalName != "materialx")
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: not a MaterialX document", filename));
            }
        }

        private static string Attribute(XElement e, string name)
        {
            XAttribute a = e.Attribute(name);
            return a != null ? a.Value : null;
        }

        private static XElement Child(XElement scope, string name)
        {
            foreach (XElement e in scope.Elements())
            {
                if (Attribute(e, "name") == name)
                {
                    return e;
                }
            }
            return null;
        }

        public static bool IsSurfaceShader(string category)
        {
            return category == "open_pbr_surface" || category == "standard_surface" || category == "gltf_pbr" || category == "UsdPreviewSurface" || category == "surface";
        }

        // The element of the surface shader of the material named (or of the first one)
        public XElement FindShader(string material)
        {
            foreach (XElement e in root.Elements())
            {
                if (e.Name.LocalName != "surfacematerial" || (material != "" && Attribute(e, "name") != material))
                {
                    continue;
                }
                foreach (XElement input in e.Elements("input"))
                {
                    if (Attribute(input, "name") == "surfaceshader" && Attribute(input, "nodename") != null)
                    {
                        XElement shader = Child(root, Attribute(input, "nodename"));
                        if (shader != null)
                        {
                            return shader;
                        }
                    }
                }
            }
            foreach (XElement e in root.Elements())
            {
                if (IsSurfaceShader(e.Name.LocalName) && (material == "" || Attribute(e, "name") == material))
                {
                    return e;
                }
            }
            pbrt.ErrorExit(loc, material == "" ? "MaterialX: no surface material in the document" : pbrt.StringPrintf("MaterialX: no material \"%s\"", material));
            return null;
        }

        private static Float[] Numbers(string text)
        {
            string[] parts = text.Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            Float[] v = new Float[parts.Length];
            for (int i = 0; i < parts.Length; ++i)
            {
                if (parts[i] == "true")
                {
                    v[i] = 1;
                }
                else if (parts[i] == "false")
                {
                    v[i] = 0;
                }
                else if (!Float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]))
                {
                    return null;
                }
            }
            return v;
        }

        private static bool IsBsdf(XElement e)
        {
            string type = Attribute(e, "type");
            return type == "BSDF" || e.Name.LocalName.EndsWith("_bsdf");
        }

        // The node that an input (or an output of a graph) is connected to, and the scope that node is in
        private XElement Connected(XElement input, XElement scope, out XElement nodeScope)
        {
            nodeScope = scope;
            string graph = Attribute(input, "nodegraph");
            if (graph != null)
            {
                XElement g = Child(root, graph);
                if (g == null)
                {
                    return null;
                }
                string output = Attribute(input, "output");
                foreach (XElement o in g.Elements("output"))
                {
                    if (output == null || Attribute(o, "name") == output)
                    {
                        return Connected(o, g, out nodeScope);
                    }
                }
                return null;
            }
            string node = Attribute(input, "nodename");
            return node != null ? Child(scope, node) : null;
        }

        private bool warnedColourSpace = false;

        // The colour space in effect for an element: its own "colorspace", or that of the nearest element around it
        private static string ColourSpace(XElement e)
        {
            for (; e != null; e = e.Parent)
            {
                string cs = Attribute(e, "colorspace");
                if (cs != null)
                {
                    return cs;
                }
            }
            return "lin_rec709";
        }

        private Float[] Decode(Float[] v, string type, string colourSpace)
        {
            if (v == null || (type != "color3" && type != "color4") || colourSpace == "lin_rec709" || colourSpace == "none" || colourSpace == "")
            {
                return v;
            }
            if (colourSpace != "srgb_texture" && colourSpace != "g22_rec709")
            {
                if (!warnedColourSpace)
                {
                    pbrt.Warning(loc, pbrt.StringPrintf("MaterialX: the colour space \"%s\" is not converted: its values are read as linear Rec. 709", colourSpace));
                    warnedColourSpace = true;
                }
                return v;
            }
            Float[] r = new Float[v.Length];
            for (int i = 0; i < v.Length; ++i)
            {
                if (i >= 3)
                {
                    r[i] = v[i];
                }
                else if (colourSpace == "g22_rec709")
                {
                    r[i] = (Float)Math.Pow(Math.Max(v[i], 0), 2.2);
                }
                else
                {
                    r[i] = v[i] <= 0.04045f ? v[i] / 12.92f : (Float)Math.Pow((v[i] + 0.055f) / 1.055f, 2.4);
                }
            }
            return r;
        }

        private static int TypeSize(string type)
        {
            switch (type)
            {
            case "vector2": return 2;
            case "color3": case "vector3": return 3;
            case "color4": case "vector4": return 4;
            default: return 1;
            }
        }

        private XElement FindNodeDef(string category)
        {
            foreach (XElement e in root.Elements("nodedef"))
            {
                if (Attribute(e, "node") == category)
                {
                    return e;
                }
            }
            return null;
        }

        // The expression of an input (or of an output of a graph): its value, the node it is connected to, or what the
        // interface of the definition being instantiated gives it
        private MtlxExpr InputExpr(XElement input, XElement scope, Dictionary<string, MtlxExpr> env, int depth)
        {
            string value = Attribute(input, "value");
            if (value != null)
            {
                Float[] numbers = Decode(Numbers(value), Attribute(input, "type"), ColourSpace(input));
                return numbers != null && numbers.Length > 0 ? MtlxExpr.Constant(numbers) : null;
            }
            string interfaceName = Attribute(input, "interfacename");
            if (interfaceName != null)
            {
                MtlxExpr bound;
                if (env != null && env.TryGetValue(interfaceName, out bound))
                {
                    return bound;
                }
                pbrt.Warning(loc, pbrt.StringPrintf("MaterialX: the interface input \"%s\" has no value: a default is used", interfaceName));
                return null;
            }
            XElement nodeScope;
            XElement node = Connected(input, scope, out nodeScope);
            if (node == null || depth > 64)
            {
                return null;
            }
            // which output of the node: named where the node itself is named (for an output of a graph, on that output)
            string selector = Attribute(input, "output");
            string graphName = Attribute(input, "nodegraph");
            if (graphName != null)
            {
                selector = null;
                XElement g = Child(root, graphName);
                if (g != null)
                {
                    foreach (XElement o in g.Elements("output"))
                    {
                        if (Attribute(input, "output") == null || Attribute(o, "name") == Attribute(input, "output"))
                        {
                            selector = Attribute(o, "output");
                            break;
                        }
                    }
                }
            }
            return NodeExpr(node, nodeScope, env, selector, depth + 1);
        }

        private MtlxExpr NodeExpr(XElement node, XElement scope, Dictionary<string, MtlxExpr> env, string output, int depth)
        {
            string category = node.Name.LocalName;
            XElement Input(string name)
            {
                foreach (XElement i in node.Elements("input"))
                {
                    if (Attribute(i, "name") == name)
                    {
                        return i;
                    }
                }
                return null;
            }
            if (category == "constant")
            {
                XElement value = Input("value");
                return value != null ? InputExpr(value, scope, env, depth) : null;
            }
            if (!MtlxExpr.Known(category))
            {
                // a node that the document defines: the graph that implements it, with its interface bound to this instance
                XElement definition = FindNodeDef(category);
                XElement graph = null;
                if (definition != null)
                {
                    foreach (XElement g in root.Elements("nodegraph"))
                    {
                        if (Attribute(g, "nodedef") == Attribute(definition, "name"))
                        {
                            graph = g;
                        }
                    }
                }
                if (graph == null)
                {
                    pbrt.Warning(loc, pbrt.StringPrintf("MaterialX: the node \"%s\" (%s) is not read: a default is used", Attribute(node, "name"), category));
                    return null;
                }
                Dictionary<string, MtlxExpr> bound = new Dictionary<string, MtlxExpr>();
                foreach (XElement declared in definition.Elements("input"))
                {
                    string name = Attribute(declared, "name");
                    XElement given = Input(name);
                    MtlxExpr e = given != null ? InputExpr(given, scope, env, depth) : null;
                    if (e == null && Attribute(declared, "value") != null)
                    {
                        e = InputExpr(declared, root, null, depth);
                    }
                    if (e != null)
                    {
                        bound[name] = e;
                    }
                }
                foreach (XElement o in graph.Elements("output"))
                {
                    if (output == null || Attribute(o, "name") == output)
                    {
                        return InputExpr(o, graph, bound, depth);
                    }
                }
                return null;
            }
            MtlxExpr expr = new MtlxExpr();
            expr.op = category;
            expr.size = TypeSize(Attribute(node, "type"));
            if (category.StartsWith("separate") && output != null && output.Length > 0)
            {
                char last = output[output.Length - 1];
                expr.index = Math.Max(Math.Max("xyzw".IndexOf(last), "rgba".IndexOf(last)), 0);
            }
            foreach (XElement input in node.Elements("input"))
            {
                string name = Attribute(input, "name"), type = Attribute(input, "type");
                if (type == "filename")
                {
                    string file = Attribute(input, "value");
                    if (file == null)
                    {
                        continue;
                    }
                    string prefix = "";
                    for (XElement e = node; e != null; e = e.Parent)
                    {
                        if (Attribute(e, "fileprefix") != null)
                        {
                            prefix = Attribute(e, "fileprefix");
                            break;
                        }
                    }
                    string path = Path.Combine(directory, prefix + file);
                    if (!File.Exists(path))
                    {
                        pbrt.Warning(loc, pbrt.StringPrintf("MaterialX: the image \"%s\" is not found: the default of the node is used", path));
                        continue;
                    }
                    string colourSpace = ColourSpace(input);
                    expr.image = Image.Read(path, alloc, colourSpace == "srgb_texture" ? ColorEncoding.sRGB : (colourSpace == "lin_rec709" || colourSpace == "none"
                                                                                                             ? (Attribute(input, "colorspace") != null || Attribute(node, "colorspace") != null ? ColorEncoding.Linear : null)
                                                                                                             : null)).image;
                    expr.rgb = expr.image.GetChannelDesc(new string[] { "R", "G", "B" });
                    continue;
                }
                if (type == "string")
                {
                    if (name == "channels" && Attribute(input, "value") != null)
                    {
                        expr.text = Attribute(input, "value");
                    }
                    continue;
                }
                MtlxExpr argument = InputExpr(input, scope, env, depth);
                if (argument != null)
                {
                    expr.args[name] = argument;
                }
            }
            return expr;
        }

        // The value of an input of a shader: a constant, or an expression to be evaluated at each point
        private MtlxInput Resolve(XElement input, XElement scope, int depth)
        {
            MtlxInput result = new MtlxInput();
            result.text = Attribute(input, "value");
            MtlxExpr expr = InputExpr(input, scope, null, depth);
            if (expr == null)
            {
                return result.text != null ? result : null;
            }
            if (expr.Varying())
            {
                result.expr = expr;
            }
            else
            {
                result.value = expr.Evaluate(default);
            }
            return result;
        }

        private static ParsedParameter Parameter(string type, string name)
        {
            ParsedParameter p = new ParsedParameter(new FileLoc());
            p.type = type;
            p.name = name;
            return p;
        }

        private string Texture(MtlxInput input, bool colour)
        {
            string name = "materialx" + (textureCount++);
            if (colour)
            {
                textures.albedoSpectrumTextures[name] = new MtlxSpectrumTexture(input.expr, false);
            }
            else
            {
                textures.floatTextures[name] = new MtlxFloatTexture(input.expr);
            }
            return name;
        }

        // { the input of the shader, the parameter of OpenPBRMaterial, its kind: f float, c colour, b bool, n nanometres to
        // micrometres, o the mean of a colour as a float, k a float that has to be a constant }
        public static string[][] Tables(string category)
        {
            switch (category)
            {
            case "open_pbr_surface":
                return new string[][] {
                    new string[] { "base_weight", "base_weight", "f" }, new string[] { "base_color", "base_color", "c" },
                    new string[] { "base_diffuse_roughness", "base_diffuse_roughness", "f" }, new string[] { "base_metalness", "base_metalness", "f" },
                    new string[] { "specular_weight", "specular_weight", "f" }, new string[] { "specular_color", "specular_color", "c" },
                    new string[] { "specular_roughness", "specular_roughness", "f" }, new string[] { "specular_ior", "specular_ior", "f" },
                    new string[] { "specular_roughness_anisotropy", "specular_roughness_anisotropy", "f" },
                    new string[] { "transmission_weight", "transmission_weight", "f" }, new string[] { "transmission_color", "transmission_color", "c" },
                    new string[] { "subsurface_weight", "subsurface_weight", "f" }, new string[] { "subsurface_color", "subsurface_color", "c" },
                    new string[] { "subsurface_scatter_anisotropy", "subsurface_scatter_anisotropy", "f" },
                    new string[] { "coat_weight", "coat_weight", "f" }, new string[] { "coat_color", "coat_color", "c" },
                    new string[] { "coat_roughness", "coat_roughness", "f" }, new string[] { "coat_roughness_anisotropy", "coat_roughness_anisotropy", "f" },
                    new string[] { "coat_ior", "coat_ior", "f" }, new string[] { "fuzz_weight", "fuzz_weight", "f" }, new string[] { "fuzz_color", "fuzz_color", "c" },
                    new string[] { "fuzz_roughness", "fuzz_roughness", "f" }, new string[] { "thin_film_weight", "thin_film_weight", "f" },
                    new string[] { "thin_film_thickness", "thin_film_thickness", "f" }, new string[] { "thin_film_ior", "thin_film_ior", "f" },
                    new string[] { "geometry_opacity", "geometry_opacity", "f" }, new string[] { "geometry_thin_walled", "geometry_thin_walled", "b" },
                    new string[] { "transmission_depth", "transmission_depth", "k" }, new string[] { "transmission_scatter", "transmission_scatter", "c" },
                    new string[] { "transmission_scatter_anisotropy", "transmission_scatter_anisotropy", "k" },
                    new string[] { "subsurface_radius", "subsurface_radius", "k" }, new string[] { "subsurface_radius_scale", "subsurface_radius_scale", "c" } };
            case "standard_surface":
                return new string[][] {
                    new string[] { "base", "base_weight", "f" }, new string[] { "base_color", "base_color", "c" },
                    new string[] { "diffuse_roughness", "base_diffuse_roughness", "f" }, new string[] { "metalness", "base_metalness", "f" },
                    new string[] { "specular", "specular_weight", "f" }, new string[] { "specular_color", "specular_color", "c" },
                    new string[] { "specular_roughness", "specular_roughness", "f" }, new string[] { "specular_IOR", "specular_ior", "f" },
                    new string[] { "specular_anisotropy", "specular_roughness_anisotropy", "f" }, new string[] { "transmission", "transmission_weight", "f" },
                    new string[] { "transmission_color", "transmission_color", "c" }, new string[] { "subsurface", "subsurface_weight", "f" },
                    new string[] { "subsurface_color", "subsurface_color", "c" }, new string[] { "subsurface_anisotropy", "subsurface_scatter_anisotropy", "f" },
                    new string[] { "sheen", "fuzz_weight", "f" }, new string[] { "sheen_color", "fuzz_color", "c" }, new string[] { "sheen_roughness", "fuzz_roughness", "f" },
                    new string[] { "coat", "coat_weight", "f" }, new string[] { "coat_color", "coat_color", "c" }, new string[] { "coat_roughness", "coat_roughness", "f" },
                    new string[] { "coat_IOR", "coat_ior", "f" }, new string[] { "coat_anisotropy", "coat_roughness_anisotropy", "f" },
                    new string[] { "thin_film_thickness", "thin_film_thickness", "n" }, new string[] { "thin_film_IOR", "thin_film_ior", "f" },
                    new string[] { "opacity", "geometry_opacity", "o" }, new string[] { "thin_walled", "geometry_thin_walled", "b" },
                    new string[] { "transmission_depth", "transmission_depth", "k" }, new string[] { "transmission_scatter", "transmission_scatter", "c" },
                    new string[] { "transmission_scatter_anisotropy", "transmission_scatter_anisotropy", "k" },
                    new string[] { "subsurface_radius", "subsurface_radius_scale", "c" }, new string[] { "subsurface_scale", "subsurface_radius", "k" } };
            case "gltf_pbr":
                return new string[][] {
                    new string[] { "base_color", "base_color", "c" }, new string[] { "metallic", "base_metalness", "f" }, new string[] { "roughness", "specular_roughness", "f" },
                    new string[] { "ior", "specular_ior", "f" }, new string[] { "specular", "specular_weight", "f" }, new string[] { "specular_color", "specular_color", "c" },
                    new string[] { "transmission", "transmission_weight", "f" }, new string[] { "attenuation_color", "transmission_color", "c" },
                    new string[] { "sheen_color", "fuzz_color", "c" }, new string[] { "sheen_roughness", "fuzz_roughness", "f" },
                    new string[] { "clearcoat", "coat_weight", "f" }, new string[] { "clearcoat_roughness", "coat_roughness", "f" },
                    new string[] { "iridescence", "thin_film_weight", "f" }, new string[] { "iridescence_ior", "thin_film_ior", "f" },
                    new string[] { "iridescence_thickness", "thin_film_thickness", "n" }, new string[] { "alpha", "geometry_opacity", "f" } };
            default:
                return new string[][] {
                    new string[] { "diffuseColor", "base_color", "c" }, new string[] { "metallic", "base_metalness", "f" }, new string[] { "roughness", "specular_roughness", "f" },
                    new string[] { "ior", "specular_ior", "f" }, new string[] { "clearcoat", "coat_weight", "f" }, new string[] { "clearcoatRoughness", "coat_roughness", "f" },
                    new string[] { "opacity", "geometry_opacity", "f" } };
            }
        }

        // The parameters of an OpenPBRMaterial for one of the surface shaders
        public List<ParsedParameter> OpenPBRParameters(XElement shader)
        {
            string category = shader.Name.LocalName;
            string[][] table = Tables(category);
            List<ParsedParameter> list = new List<ParsedParameter>();
            bool sheenGiven = false, filmGiven = false;
            foreach (XElement input in shader.Elements("input"))
            {
                string name = Attribute(input, "name");
                string[] row = null;
                for (int i = 0; i < table.Length; ++i)
                {
                    if (table[i][0] == name)
                    {
                        row = table[i];
                    }
                }
                if (row == null)
                {
                    continue;
                }
                MtlxInput v = Resolve(input, root, 0);
                if (v == null)
                {
                    continue;
                }
                string kind = row[2];
                if (!v.IsConstant())
                {
                    if (kind != "f" && kind != "c")
                    {
                        pbrt.Warning(loc, pbrt.StringPrintf("MaterialX: the input \"%s\" has to be a constant here: its default is used", name));
                        continue;
                    }
                    ParsedParameter t = Parameter("texture", row[1]);
                    t.AddString(Texture(v, kind == "c"));
                    list.Add(t);
                    sheenGiven |= row[1] == "fuzz_color";
                    continue;
                }
                if (v.value == null || v.value.Length == 0)
                {
                    continue;
                }
                ParsedParameter p;
                switch (kind)
                {
                case "c":
                    p = Parameter("rgb", row[1]);
                    for (int c = 0; c < 3; ++c)
                    {
                        p.AddFloat(std.max<Float>(v.value[Math.Min(c, v.value.Length - 1)], 0));
                    }
                    sheenGiven |= row[1] == "fuzz_color" && v.value[0] + v.value[Math.Min(1, v.value.Length - 1)] + v.value[Math.Min(2, v.value.Length - 1)] > 0;
                    break;
                case "b":
                    p = Parameter("bool", row[1]);
                    p.AddBool(v.value[0] != 0);
                    break;
                case "n":
                    p = Parameter("float", row[1]);
                    p.AddFloat(v.value[0] / 1000);
                    filmGiven |= v.value[0] > 0;
                    break;
                case "o":
                    {
                        Float mean = 0;
                        for (int c = 0; c < v.value.Length; ++c)
                        {
                            mean += v.value[c] / v.value.Length;
                        }
                        p = Parameter("float", row[1]);
                        p.AddFloat(mean);
                    }
                    break;
                default:
                    p = Parameter("float", row[1]);
                    p.AddFloat(v.value[0]);
                    break;
                }
                list.Add(p);
            }
            // What the other models leave implicit
            if (category == "standard_surface" && filmGiven)
            {
                ParsedParameter p = Parameter("float", "thin_film_weight");
                p.AddFloat(1);
                list.Add(p);
            }
            if (category == "gltf_pbr")
            {
                if (sheenGiven)
                {
                    ParsedParameter p = Parameter("float", "fuzz_weight");
                    p.AddFloat(1);
                    list.Add(p);
                }
                ParsedParameter ior = Parameter("float", "coat_ior");
                ior.AddFloat(1.5f);
                list.Add(ior);
            }
            if (category == "UsdPreviewSurface")
            {
                // (its roughness is the square root of the alpha, as OpenPBR's; its base has no separate weight)
                ParsedParameter ior = Parameter("float", "coat_ior");
                ior.AddFloat(1.5f);
                list.Add(ior);
            }
            return list;
        }

        // The tree of BSDF nodes under an input of a <surface> node or of a BSDF node
        public MtlxNode Graph(XElement element, XElement scope, int depth)
        {
            MtlxNode node = new MtlxNode();
            node.category = element.Name.LocalName;
            node.name = Attribute(element, "name");
            if (depth > 64)
            {
                return node;
            }
            foreach (XElement input in element.Elements("input"))
            {
                string name = Attribute(input, "name");
                XElement childScope = scope;
                XElement child = null;
                if (Attribute(input, "value") == null)
                {
                    child = Connected(input, scope, out childScope);
                }
                if (child != null && IsBsdf(child))
                {
                    node.links[name] = Graph(child, childScope, depth + 1);
                    continue;
                }
                MtlxInput v = Resolve(input, scope, 0);
                if (v == null)
                {
                    continue;
                }
                if (!v.IsConstant())
                {
                    string type = Attribute(input, "type");
                    if (type == "color3" || type == "color4" || type == "vector3")
                    {
                        v.spectrumTexture = new MtlxSpectrumTexture(v.expr, name == "ior" || name == "extinction" || name == "absorption_coefficient");
                    }
                    else
                    {
                        v.floatTexture = new MtlxFloatTexture(v.expr);
                    }
                }
                node.inputs[name] = v;
            }
            return node;
        }

        public XElement Root() { return root; }
    }

    // MaterialXMaterial Definition
    // A <surface> of the physically based shading library: the tree of its BSDF nodes
    public sealed unsafe class MaterialXMaterial : Material
    {
        public MtlxNode surface;
        public int scatterDepth = 16, maxDepth = 32, nSamples = 1;
        public FloatTexture displacement;
        public Image normalMap;
        private List<FloatTexture> floats = new List<FloatTexture>();
        private List<SpectrumTexture> spectra = new List<SpectrumTexture>();

        public static string Name() { return "MaterialXMaterial"; }

        public override FloatTexture GetDisplacement() { return displacement; }
        public override Image GetNormalMap() { return normalMap; }
        public override bool HasSubsurfaceScattering() { return false; }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return null;
        }

        private void Gather(MtlxNode node)
        {
            foreach (MtlxInput i in node.inputs.Values)
            {
                if (i.floatTexture != null)
                {
                    floats.Add(i.floatTexture);
                }
                if (i.spectrumTexture != null)
                {
                    spectra.Add(i.spectrumTexture);
                }
            }
            foreach (MtlxNode child in node.links.Values)
            {
                Gather(child);
            }
        }

        public MaterialXMaterial(MtlxNode surface)
        {
            this.surface = surface;
            Gather(surface);
        }

        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval)
        {
            return texEval.CanEvaluate(floats.ToArray(), spectra.ToArray());
        }

        public override string ToString() { return StringPrintf("[ MaterialXMaterial surface: %s ]", surface.name); }

        private sealed class Point<TextureEvaluator>
            where TextureEvaluator : ITextureEvaluator
        {
            public TextureEvaluator texEval;
            public MaterialEvalContext ctx;
            public SampledWavelengths lambda;
        }

        private static Float F<TextureEvaluator>(Point<TextureEvaluator> p, MtlxNode node, string name, Float fallback)
            where TextureEvaluator : ITextureEvaluator
        {
            MtlxInput i;
            if (!node.inputs.TryGetValue(name, out i))
            {
                return fallback;
            }
            if (i.floatTexture != null)
            {
                return p.texEval.Apply(i.floatTexture, p.ctx);
            }
            return i.value != null && i.value.Length > 0 ? i.value[0] : fallback;
        }

        // A colour as a reflectance (bounded) or, with `unbounded`, as an index
        private static SampledSpectrum C<TextureEvaluator>(Point<TextureEvaluator> p, MtlxNode node, string name, Float fallback, bool unbounded)
            where TextureEvaluator : ITextureEvaluator
        {
            MtlxInput i;
            if (!node.inputs.TryGetValue(name, out i))
            {
                return new SampledSpectrum(fallback);
            }
            if (i.spectrumTexture != null)
            {
                return p.texEval.Apply(i.spectrumTexture, p.ctx, p.lambda);
            }
            if (i.floatTexture != null)
            {
                return new SampledSpectrum(p.texEval.Apply(i.floatTexture, p.ctx));
            }
            if (i.value == null || i.value.Length == 0)
            {
                return new SampledSpectrum(fallback);
            }
            if (i.value.Length < 3)
            {
                return new SampledSpectrum(i.value[0]);
            }
            RGB rgb = new RGB(std.max<Float>(i.value[0], 0), std.max<Float>(i.value[1], 0), std.max<Float>(i.value[2], 0));
            if (unbounded)
            {
                return new RGBUnboundedSpectrum(RGBColorSpace.sRGB, rgb).Sample(p.lambda);
            }
            rgb = new RGB(std.min<Float>(rgb.r, 1), std.min<Float>(rgb.g, 1), std.min<Float>(rgb.b, 1));
            return new RGBAlbedoSpectrum(RGBColorSpace.sRGB, rgb).Sample(p.lambda);
        }

        private static string Text(MtlxNode node, string name, string fallback)
        {
            MtlxInput i;
            return node.inputs.TryGetValue(name, out i) && i.text != null ? i.text : fallback;
        }

        // roughness: a vector2 of alphas
        private static MicrofacetDistribution Distribution<TextureEvaluator>(Point<TextureEvaluator> p, MtlxNode node)
            where TextureEvaluator : ITextureEvaluator
        {
            Float ax = 0.05f, ay = 0.05f;
            MtlxInput i;
            if (node.inputs.TryGetValue("roughness", out i))
            {
                if (i.floatTexture != null)
                {
                    ax = ay = p.texEval.Apply(i.floatTexture, p.ctx);
                }
                else if (i.value != null && i.value.Length > 0)
                {
                    ax = i.value[0];
                    ay = i.value[Math.Min(1, i.value.Length - 1)];
                }
            }
            return new TrowbridgeReitzDistribution(Clamp(ax, 0, 1), Clamp(ay, 0, 1));
        }

        private static bool IsOne(SampledSpectrum s)
        {
            for (int c = 0; c < NSpectrumSamples; ++c)
            {
                if (s[c] != 1)
                {
                    return false;
                }
            }
            return true;
        }

        private static BxDF Scaled(BxDF lobe, SampledSpectrum reflection, SampledSpectrum transmission)
        {
            return IsOne(reflection) && IsOne(transmission) ? lobe : new TintedBxDF(lobe, reflection, transmission);
        }

        // `interface`: the node is the top of a layer and what it does not reflect goes on to the base
        private BxDF Build<TextureEvaluator>(Point<TextureEvaluator> p, MtlxNode node, bool asInterface, bool film, ThinFilmStack stack)
            where TextureEvaluator : ITextureEvaluator
        {
            SampledSpectrum one = new SampledSpectrum(1f), zero = new SampledSpectrum(0f);
            Float weight = Clamp(F(p, node, "weight", 1), 0, 1);
            switch (node.category)
            {
            case "oren_nayar_diffuse_bsdf":
                return new EonDiffuseBxDF(Clamp(C(p, node, "color", 0.18f, false) * weight, 0, 1), Clamp(F(p, node, "roughness", 0), 0, 1));
            case "burley_diffuse_bsdf":
                return new TintedBxDF(new OSLBurleyDiffuseBxDF(F(p, node, "roughness", 0)), Clamp(C(p, node, "color", 0.18f, false) * weight, 0, 1), zero);
            case "translucent_bsdf":
                return new DiffuseTransmissionBxDF(zero, Clamp(C(p, node, "color", 1, false) * weight, 0, 1));
            case "subsurface_bsdf":
                {
                    SampledSpectrum albedo = OpenPBRMaterial.SubsurfaceAlbedo(Clamp(C(p, node, "color", 0.18f, false), 0, 1));
                    BxDF slab = new StackBxDF(new OSLTransparentBxDF(), new DiffuseBxDF(zero), 30, albedo, Clamp(F(p, node, "anisotropy", 0), -0.99f, 0.99f),
                                              std.max(maxDepth, 256), nSamples, true);
                    return Scaled(slab, new SampledSpectrum(weight), one);
                }
            case "sheen_bsdf":
                return new SGGXSheenBxDF(Clamp(C(p, node, "color", 1, false) * weight, 0, 1), Clamp(F(p, node, "roughness", 0.3f), 0.01f, 1), 1, std.max(scatterDepth, 16));
            case "conductor_bsdf":
                {
                    MicrofacetDistribution d = Distribution(p, node);
                    SampledSpectrum eta = C(p, node, "ior", 0.27f, true), k = C(p, node, "extinction", 3.6f, true);
                    BxDF lobe = film ? new ThinFilmBxDF(stack, 1, eta, k, true, d) : new ConductorBxDF(d, eta, k);
                    return Scaled(lobe, new SampledSpectrum(weight), one);
                }
            case "dielectric_bsdf":
                {
                    MicrofacetDistribution d = Distribution(p, node);
                    Float ior = std.max<Float>(F(p, node, "ior", 1.5f), 1.0001f);
                    SampledSpectrum tint = Clamp(C(p, node, "tint", 1, false) * weight, 0, 1);
                    string mode = Text(node, "scatter_mode", "R");
                    BxDF lobe = film ? new ThinFilmBxDF(stack, ior, one, zero, false, d) : new DielectricBxDF(ior, d);
                    if (asInterface)
                    {
                        return Scaled(lobe, tint, one);
                    }
                    return Scaled(lobe, mode == "T" ? zero : tint, mode == "R" ? zero : tint);
                }
            case "generalized_schlick_bsdf":
                {
                    MicrofacetDistribution d = Distribution(p, node);
                    SampledSpectrum c0 = Clamp(C(p, node, "color0", 1, false), 0, 1), c90 = Clamp(C(p, node, "color90", 1, false), 0, 1);
                    if (asInterface && c0.Average() <= 0.3f)
                    {
                        // the dielectric with this reflectance along the normal
                        Float root = std.sqrt(std.max<Float>(c0.Average(), 1e-6f));
                        return Scaled(new DielectricBxDF(std.max<Float>((1 + root) / (1 - root), 1.0001f), d), new SampledSpectrum(weight), one);
                    }
                    return Scaled(new GeneralizedSchlickBxDF(d, c0, c90, std.max<Float>(F(p, node, "exponent", 5), 0.1f), 0), new SampledSpectrum(weight), one);
                }
            case "chiang_hair_bsdf":
                {
                    // (the model of Chiang et al. as pbrt has it: one roughness along the hair and one around it, for the
                    // three lobes, and no tints)
                    MtlxInput roughness;
                    Float along = 0.3f, around = 0.3f;
                    if (node.inputs.TryGetValue("roughness_R", out roughness) && roughness.value != null && roughness.value.Length > 0)
                    {
                        along = roughness.value[0];
                        around = roughness.value[Math.Min(1, roughness.value.Length - 1)];
                    }
                    Float h = -1 + 2 * p.ctx.uv[1];
                    return new HairBxDF(h, std.max<Float>(F(p, node, "ior", 1.55f), 1.0001f), C(p, node, "absorption_coefficient", 0.25f, true), Clamp(along, 0.01f, 1),
                                        Clamp(around, 0.01f, 1), (F(p, node, "cuticle_angle", 0.5f) - 0.5f) * 180);
                }
            case "mix":
                {
                    MtlxNode fg, bg;
                    node.links.TryGetValue("fg", out fg);
                    node.links.TryGetValue("bg", out bg);
                    Float mix = Clamp(F(p, node, "mix", 0), 0, 1);
                    return MixBxDF.Of(bg != null ? Build(p, bg, asInterface, film, stack) : null, fg != null ? Build(p, fg, asInterface, film, stack) : null, mix) ??
                           new DiffuseBxDF(zero);
                }
            case "add":
                {
                    MtlxNode a, b;
                    node.links.TryGetValue("in1", out a);
                    node.links.TryGetValue("in2", out b);
                    if (a == null || b == null)
                    {
                        return a != null ? Build(p, a, asInterface, film, stack) : b != null ? Build(p, b, asInterface, film, stack) : new DiffuseBxDF(zero);
                    }
                    return new MixBxDF(new BxDF[] { Build(p, a, asInterface, film, stack), Build(p, b, asInterface, film, stack) }, new Float[] { 1, 1 });
                }
            case "multiply":
                {
                    MtlxNode a;
                    if (!node.links.TryGetValue("in1", out a))
                    {
                        return new DiffuseBxDF(zero);
                    }
                    SampledSpectrum factor = Clamp(C(p, node, "in2", 1, false), 0, 1);
                    return Scaled(Build(p, a, asInterface, film, stack), factor, factor);
                }
            case "layer":
                {
                    MtlxNode top, lower;
                    node.links.TryGetValue("top", out top);
                    node.links.TryGetValue("base", out lower);
                    if (top == null || lower == null)
                    {
                        return top != null ? Build(p, top, asInterface, film, stack) : lower != null ? Build(p, lower, asInterface, film, stack) : new DiffuseBxDF(zero);
                    }
                    if (top.category == "thin_film_bsdf")
                    {
                        Float thickness = F(p, top, "thickness", 550), ior = F(p, top, "ior", 1.5f);
                        if (!(thickness > 0))
                        {
                            return Build(p, lower, asInterface, film, stack);
                        }
                        return Build(p, lower, asInterface, true, new ThinFilmStack(p.lambda, new Float[] { ior }, new Float[] { thickness }));
                    }
                    BxDF above = Build(p, top, true, film, stack);
                    if ((above.Flags() & BxDFFlags.Transmission) == 0)
                    {
                        return above;  // an opaque top: nothing of the base is seen
                    }
                    BxDF below = Build(p, lower, false, false, default);
                    return StackBxDF.Absorbing(above, below, zero, maxDepth, nSamples, (below.Flags() & BxDFFlags.Transmission) == 0);
                }
            default:
                Warning(StringPrintf("MaterialX: the BSDF node \"%s\" (%s) is not read: it is black", node.name, node.category));
                return new DiffuseBxDF(zero);
            }
        }

        public BxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda)
            where TextureEvaluator : ITextureEvaluator
        {
            Point<TextureEvaluator> p = new Point<TextureEvaluator>();
            p.texEval = texEval;
            p.ctx = ctx;
            p.lambda = lambda;
            MtlxNode bsdf;
            BxDF result = surface.links.TryGetValue("bsdf", out bsdf) ? Build(p, bsdf, false, false, default) : new DiffuseBxDF(new SampledSpectrum(0f));
            Float opacity = Clamp(F(p, surface, "opacity", 1), 0, 1);
            return opacity < 1 ? MixBxDF.Of(new OSLTransparentBxDF(), result, opacity) : result;
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(GetBxDF(texEval, ctx, ref lambda)));
        }

        // The material of a document: an OpenPBRMaterial for a surface shader, a MaterialXMaterial for a <surface>
        public static Material Create(TextureParameterDictionary parameters, Image normalMap, FileLoc loc, Allocator alloc)
        {
            string filename = ResolveFilename(parameters.GetOneString("filename", ""));
            if (filename == "")
            {
                ErrorExit(loc, "\"materialx\": \"string filename\" is required");
            }
            MaterialXReader reader = new MaterialXReader(filename, loc, alloc);
            System.Xml.Linq.XElement shader = reader.FindShader(parameters.GetOneString("material", ""));
            string category = shader.Name.LocalName;
            if (category == "surface")
            {
                MaterialXMaterial material = new MaterialXMaterial(reader.Graph(shader, reader.Root(), 0));
                material.scatterDepth = parameters.GetOneInt("scatterdepth", 16);
                material.maxDepth = parameters.GetOneInt("maxdepth", 32);
                material.nSamples = parameters.GetOneInt("nsamples", 1);
                material.displacement = parameters.GetFloatTextureOrNull("displacement", alloc);
                material.normalMap = normalMap;
                return material;
            }
            if (!MaterialXReader.IsSurfaceShader(category))
            {
                ErrorExit(loc, StringPrintf("MaterialX: the surface shader \"%s\" is not read (open_pbr_surface, standard_surface, gltf_pbr, UsdPreviewSurface, surface)",
                                            category));
            }
            List<ParsedParameter> list = reader.OpenPBRParameters(shader);
            // the options of OpenPBRMaterial that the document has no input for
            foreach (string prefix in new string[] { "specular_", "metal_", "coat_" })
            {
                foreach (string option in new string[] { "lobe", "distribution" })
                {
                    string value = parameters.GetOneString(prefix + option, "");
                    if (value != "")
                    {
                        ParsedParameter p = new ParsedParameter(new FileLoc());
                        p.type = "string";
                        p.name = prefix + option;
                        p.AddString(value);
                        list.Add(p);
                    }
                }
            }
            foreach (string option in new string[] { "subsurface_mode", "volume_scattering" })
            {
                string value = parameters.GetOneString(option, "");
                if (value != "")
                {
                    ParsedParameter p = new ParsedParameter(new FileLoc());
                    p.type = "string";
                    p.name = option;
                    p.AddString(value);
                    list.Add(p);
                }
            }
            foreach (string option in new string[] { "scatterdepth", "maxdepth", "nsamples" })
            {
                int value = parameters.GetOneInt(option, -1);
                if (value >= 0)
                {
                    ParsedParameter p = new ParsedParameter(new FileLoc());
                    p.type = "integer";
                    p.name = option;
                    p.AddInt(value);
                    list.Add(p);
                }
            }
            TextureParameterDictionary d = new TextureParameterDictionary(new ParameterDictionary(list, RGBColorSpace.sRGB), reader.textures);
            return OpenPBRMaterial.Create(d, normalMap, loc, alloc);
        }
    }
}
