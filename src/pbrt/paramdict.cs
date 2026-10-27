// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// paramdict.h, paramdict.cpp

using System;
using System.Collections.Generic;
using System.Text;

namespace pbrt
{
    public static unsafe partial class pbrt
    {
        public static string ToString(SpectrumType t)
        {
            switch (t)
            {
            case SpectrumType.Albedo:
                return "Albedo";
            case SpectrumType.Unbounded:
                return "Unbounded";
            case SpectrumType.Illuminant:
                return "Illuminant";
            default:
                LOG_FATAL("Unhandled SpectrumType");
                return "";
            }
        }
    }


    // ParsedParameter Definition
    // (ParsedParameter::AddFloat/AddInt/AddString/AddBool/ToString are defined in parser.cpp -> parser.cs)
    public sealed unsafe partial class ParsedParameter
    {
        // ParsedParameter Public Methods
        public ParsedParameter(FileLoc loc) { this.loc = loc; }

        // ParsedParameter Public Members
        public string type, name;
        public FileLoc loc;
        public pstd.vector<Float> floats = new pstd.vector<Float>();
        public pstd.vector<int> ints = new pstd.vector<int>();
        public pstd.vector<string> strings = new pstd.vector<string>();
        public pstd.vector<byte> bools = new pstd.vector<byte>();
        public bool lookedUp = false;
        public RGBColorSpace colorSpace = null;
        public bool mayBeUnused = false;
    }

    // ParameterType Definition
    public enum ParameterType
    {
        Boolean,
        Float,
        Integer,
        Point2f,
        Vector2f,
        Point3f,
        Vector3f,
        Normal3f,
        Spectrum,
        String,
        Texture
    }

    // SpectrumType Definition
    public enum SpectrumType { Illuminant, Albedo, Unbounded }

    // NamedTextures Definition
    public sealed class NamedTextures
    {
        public SortedDictionary<string, FloatTexture> floatTextures = new SortedDictionary<string, FloatTexture>(StringComparer.Ordinal);
        public SortedDictionary<string, SpectrumTexture> albedoSpectrumTextures = new SortedDictionary<string, SpectrumTexture>(StringComparer.Ordinal);
        public SortedDictionary<string, SpectrumTexture> unboundedSpectrumTextures = new SortedDictionary<string, SpectrumTexture>(StringComparer.Ordinal);
        public SortedDictionary<string, SpectrumTexture> illuminantSpectrumTextures = new SortedDictionary<string, SpectrumTexture>(StringComparer.Ordinal);
    }

    // ParameterTypeTraits<PT>: one instance per ParameterType; "ReturnType" is T,
    // Convert(v, loc) is convert(param, firstIndex).
    internal sealed class ParameterTypeTraits<T>
    {
        public string typeName;
        public int nPerItem;
        public Func<ParsedParameter, ulong> nValues;           // GetValues(param).size()
        public Func<ParsedParameter, int, T> convert;           // Convert(&values[i], &loc)
    }

    // ParameterDictionary Definition
    public sealed unsafe class ParameterDictionary
    {
        // ParameterType traits (ParameterTypeTraits<ParameterType::...> specializations)
        private static readonly ParameterTypeTraits<byte> BooleanTraits = new ParameterTypeTraits<byte>
        {
            typeName = "bool", nPerItem = 1, nValues = (ParsedParameter p) => p.bools.size(),
            convert = (ParsedParameter p, int i) => p.bools[i]
        };
        private static readonly ParameterTypeTraits<Float> FloatTraits = new ParameterTypeTraits<Float>
        {
            typeName = "float", nPerItem = 1, nValues = (ParsedParameter p) => p.floats.size(),
            convert = (ParsedParameter p, int i) => p.floats[i]
        };
        private static readonly ParameterTypeTraits<int> IntegerTraits = new ParameterTypeTraits<int>
        {
            typeName = "integer", nPerItem = 1, nValues = (ParsedParameter p) => p.ints.size(),
            convert = (ParsedParameter p, int i) => p.ints[i]
        };
        private static readonly ParameterTypeTraits<Point2f> Point2fTraits = new ParameterTypeTraits<Point2f>
        {
            typeName = "point2", nPerItem = 2, nValues = (ParsedParameter p) => p.floats.size(),
            convert = (ParsedParameter p, int i) => new Point2f(p.floats[i], p.floats[i + 1])
        };
        private static readonly ParameterTypeTraits<Vector2f> Vector2fTraits = new ParameterTypeTraits<Vector2f>
        {
            typeName = "vector2", nPerItem = 2, nValues = (ParsedParameter p) => p.floats.size(),
            convert = (ParsedParameter p, int i) => new Vector2f(p.floats[i], p.floats[i + 1])
        };
        private static readonly ParameterTypeTraits<Point3f> Point3fTraits = new ParameterTypeTraits<Point3f>
        {
            typeName = "point3", nPerItem = 3, nValues = (ParsedParameter p) => p.floats.size(),
            convert = (ParsedParameter p, int i) => new Point3f(p.floats[i], p.floats[i + 1], p.floats[i + 2])
        };
        private static readonly ParameterTypeTraits<Vector3f> Vector3fTraits = new ParameterTypeTraits<Vector3f>
        {
            typeName = "vector3", nPerItem = 3, nValues = (ParsedParameter p) => p.floats.size(),
            convert = (ParsedParameter p, int i) => new Vector3f(p.floats[i], p.floats[i + 1], p.floats[i + 2])
        };
        private static readonly ParameterTypeTraits<Normal3f> Normal3fTraits = new ParameterTypeTraits<Normal3f>
        {
            typeName = "normal", nPerItem = 3, nValues = (ParsedParameter p) => p.floats.size(),
            convert = (ParsedParameter p, int i) => new Normal3f(p.floats[i], p.floats[i + 1], p.floats[i + 2])
        };
        private static readonly ParameterTypeTraits<string> StringTraits = new ParameterTypeTraits<string>
        {
            typeName = "string", nPerItem = 1, nValues = (ParsedParameter p) => p.strings.size(),
            convert = (ParsedParameter p, int i) => p.strings[i]
        };

        // ParameterDictionary Public Methods
        public ParameterDictionary() { }
        public ParameterDictionary(List<ParsedParameter> p, RGBColorSpace colorSpace)
        {
            @params = new List<ParsedParameter>(p);
            this.colorSpace = colorSpace;
            nOwnedParams = @params.Count;
            @params.Reverse();
            pbrt.CHECK(colorSpace != null);
            checkParameterTypes();
        }

        public ParameterDictionary(List<ParsedParameter> params0, List<ParsedParameter> params1, RGBColorSpace colorSpace)
        {
            @params = new List<ParsedParameter>(params0);
            this.colorSpace = colorSpace;
            nOwnedParams = @params.Count;
            @params.Reverse();
            pbrt.CHECK(colorSpace != null);
            for (int i = params1.Count - 1; i >= 0; --i)
                @params.Add(params1[i]);
            checkParameterTypes();
        }

        private void checkParameterTypes()
        {
            foreach (ParsedParameter p in @params)
            {
                if (p.type == BooleanTraits.typeName)
                {
                    if (p.bools.empty())
                        pbrt.ErrorExit(p.loc, pbrt.StringPrintf("\"%s\": non-Boolean values provided for Boolean-valued parameter", p.name));
                }
                else if (p.type == FloatTraits.typeName || p.type == IntegerTraits.typeName ||
                         p.type == Point2fTraits.typeName || p.type == Vector2fTraits.typeName ||
                         p.type == Point3fTraits.typeName || p.type == Vector3fTraits.typeName ||
                         p.type == Normal3fTraits.typeName || p.type == "rgb" || p.type == "blackbody")
                {
                    if (p.ints.empty() && p.floats.empty())
                        pbrt.ErrorExit(p.loc, pbrt.StringPrintf("\"%s\": non-numeric values provided for numeric-valued parameter", p.name));
                }
                else if (p.type == StringTraits.typeName || p.type == "texture")
                {
                    if (p.strings.empty())
                        pbrt.ErrorExit(p.loc, pbrt.StringPrintf("\"%s\": non-string values provided for string-valued parameter", p.name));
                }
                else if (p.type == "spectrum")
                {
                    if (p.strings.empty() && p.ints.empty() && p.floats.empty())
                        pbrt.ErrorExit(p.loc, pbrt.StringPrintf("\"%s\": expecting string or numeric-valued parameter for spectrum parameter", p.name));
                }
                else
                    pbrt.ErrorExit(p.loc, pbrt.StringPrintf("\"%s\": unknown parameter type", p.type));
            }
        }
        public Point3f GetOnePoint3f(string name, Point3f def) { return lookupSingle(name, def, Point3fTraits); }

        public void FreeParameters()
        {
            // (GC-managed; the C++ code deletes the owned ParsedParameters)
            @params.Clear();
        }

        public Float GetOneFloat(string name, Float def) { return lookupSingle(name, def, FloatTraits); }
        public int GetOneInt(string name, int def) { return lookupSingle(name, def, IntegerTraits); }
        public bool GetOneBool(string name, bool def)
        {
            return lookupSingle(name, (byte)(def ? 1 : 0), BooleanTraits) != 0;
        }

        public Point2f GetOnePoint2f(string name, Point2f def) { return lookupSingle(name, def, Point2fTraits); }
        public Vector2f GetOneVector2f(string name, Vector2f def) { return lookupSingle(name, def, Vector2fTraits); }
        public Vector3f GetOneVector3f(string name, Vector3f def) { return lookupSingle(name, def, Vector3fTraits); }
        public Normal3f GetOneNormal3f(string name, Normal3f def) { return lookupSingle(name, def, Normal3fTraits); }

        public Spectrum GetOneSpectrum(string name, Spectrum defaultValue, SpectrumType spectrumType, Allocator alloc)
        {
            foreach (ParsedParameter p in @params)
            {
                if (p.name != name)
                    continue;

                List<Spectrum> s = extractSpectrumArray(p, spectrumType, alloc);
                if (s.Count != 0)
                {
                    if (s.Count > 1)
                        pbrt.ErrorExit(p.loc, pbrt.StringPrintf("More than one value provided for parameter \"%s\".", name));
                    return s[0];
                }
            }
            return defaultValue;
        }
        public string GetOneString(string name, string def) { return lookupSingle(name, def, StringTraits); }

        public List<Float> GetFloatArray(string name) { return lookupArray(name, FloatTraits); }
        public List<int> GetIntArray(string name) { return lookupArray(name, IntegerTraits); }
        public List<byte> GetBoolArray(string name) { return lookupArray(name, BooleanTraits); }

        public List<Point2f> GetPoint2fArray(string name) { return lookupArray(name, Point2fTraits); }
        public List<Vector2f> GetVector2fArray(string name) { return lookupArray(name, Vector2fTraits); }

        public RGBColorSpace ColorSpace() { return colorSpace; }
        public List<Point3f> GetPoint3fArray(string name) { return lookupArray(name, Point3fTraits); }
        public List<Vector3f> GetVector3fArray(string name) { return lookupArray(name, Vector3fTraits); }
        public List<Normal3f> GetNormal3fArray(string name) { return lookupArray(name, Normal3fTraits); }

        private List<Spectrum> extractSpectrumArray(ParsedParameter param, SpectrumType spectrumType, Allocator alloc)
        {
            if (param.type == "rgb" || (pbrt.Options.upgrade && param.type == "color"))
                return returnArray<Spectrum>(param.floats.size(), param, 3, (ParsedParameter pp, int i) =>
                {
                    RGB rgb = new RGB(pp.floats[i], pp.floats[i + 1], pp.floats[i + 2]);
                    RGBColorSpace cs = param.colorSpace != null ? param.colorSpace : colorSpace;
                    if (rgb.r < 0 || rgb.g < 0 || rgb.b < 0)
                        pbrt.ErrorExit(param.loc, pbrt.StringPrintf("RGB parameter \"%s\" has negative component.", param.name));

                    if (spectrumType == SpectrumType.Albedo)
                    {
                        if (rgb.r > 1 || rgb.g > 1 || rgb.b > 1)
                            pbrt.ErrorExit(param.loc, pbrt.StringPrintf("RGB parameter \"%s\" has > 1 component.", param.name));
                        return new RGBAlbedoSpectrum(cs, rgb);
                    }
                    else if (spectrumType == SpectrumType.Unbounded)
                    {
                        return new RGBUnboundedSpectrum(cs, rgb);
                    }
                    else
                    {
                        pbrt.CHECK(spectrumType == SpectrumType.Illuminant);
                        return new RGBIlluminantSpectrum(cs, rgb);
                    }
                });
            else if (param.type == "blackbody")
                return returnArray<Spectrum>(param.floats.size(), param, 1, (ParsedParameter pp, int i) =>
                {
                    return new BlackbodySpectrum(pp.floats[i]);
                });
            else if (param.type == "spectrum" && !param.floats.empty())
            {
                if (param.floats.size() % 2 != 0)
                    pbrt.ErrorExit(param.loc, pbrt.StringPrintf("Found odd number of values for \"%s\"", param.name));
                int nSamples = (int)(param.floats.size() / 2);
                if (nSamples == 1)
                    pbrt.Warning(param.loc, "Specified spectrum is only non-zero at a single wavelength. This is probably unintended.");
                return returnArray<Spectrum>(param.floats.size(), param, (int)param.floats.size(), (ParsedParameter pp, int i) =>
                {
                    Float[] lambda = new Float[nSamples], value = new Float[nSamples];
                    for (int k = 0; k < nSamples; ++k)
                    {
                        if (k > 0 && pp.floats[i + 2 * k] <= lambda[k - 1])
                            pbrt.ErrorExit(param.loc, pbrt.StringPrintf("Spectrum description invalid: at %d'th entry, " +
                                              "wavelengths aren't increasing: %f >= %f.", k - 1, lambda[k - 1], pp.floats[i + 2 * k]));
                        lambda[k] = pp.floats[i + 2 * k];
                        value[k] = pp.floats[i + 2 * k + 1];
                    }
                    return new PiecewiseLinearSpectrum(lambda, value, alloc);
                });
            }
            else if (param.type == "spectrum" && !param.strings.empty())
                return returnArray<Spectrum>(param.strings.size(), param, 1, (ParsedParameter pp, int i) =>
                {
                    string s = pp.strings[i];
                    Spectrum spd = pbrt.GetNamedSpectrum(s);
                    if (spd != null)
                        return spd;

                    spd = readSpectrumFromFile(s, alloc);
                    if (spd == null)
                        pbrt.ErrorExit(param.loc, pbrt.StringPrintf("%s: unable to read valid spectrum file", s));
                    return spd;
                });
            return new List<Spectrum>();
        }

        public List<ParsedParameter> GetParameterVector() { return @params; }
        public List<Spectrum> GetSpectrumArray(string name, SpectrumType spectrumType, Allocator alloc)
        {
            foreach (ParsedParameter p in @params)
            {
                if (p.name != name)
                    continue;

                List<Spectrum> s = extractSpectrumArray(p, spectrumType, alloc);
                if (s.Count != 0)
                    return s;
            }
            return new List<Spectrum>();
        }
        public List<string> GetStringArray(string name) { return lookupArray(name, StringTraits); }

        public string GetTexture(string name)
        {
            foreach (ParsedParameter p in @params)
            {
                if (p.name != name || p.type != "texture")
                    continue;

                if (p.strings.empty())
                    pbrt.ErrorExit(p.loc, pbrt.StringPrintf("No string values provided for parameter \"%s\".", name));
                if (p.strings.size() > 1)
                    pbrt.ErrorExit(p.loc, pbrt.StringPrintf("More than one value provided for parameter \"%s\".", name));

                p.lookedUp = true;
                return p.strings[0];
            }

            return "";
        }

        public List<RGB> GetRGBArray(string name)
        {
            foreach (ParsedParameter p in @params)
            {
                if (p.name == name && p.type == "rgb")
                {
                    if (p.floats.size() % 3 != 0)
                        pbrt.ErrorExit(p.loc, "Number of values given for \"rgb\" parameter \"" + name + "\" isn't a multiple of 3.");
                    List<RGB> rgb = new List<RGB>((int)(p.floats.size() / 3));
                    for (int i = 0; i < (int)(p.floats.size() / 3); ++i)
                        rgb.Add(new RGB(p.floats[3 * i], p.floats[3 * i + 1], p.floats[3 * i + 2]));
                    p.lookedUp = true;
                    return rgb;
                }
            }
            return new List<RGB>();
        }

        // For --upgrade only
        public pstd.optional<RGB> GetOneRGB(string name)
        {
            foreach (ParsedParameter p in @params)
            {
                if (p.name == name && p.type == "rgb")
                {
                    if (p.floats.size() < 3)
                        pbrt.ErrorExit(p.loc, pbrt.StringPrintf("Insufficient values for \"rgb\" parameter \"%s\".", p.name));
                    return new RGB(p.floats[0], p.floats[1], p.floats[2]);
                }
            }
            return default;
        }

        // Unfortunately, this is most easily done here...
        public Float UpgradeBlackbody(string name)
        {
            Float scale = 1;
            foreach (ParsedParameter p in @params)
            {
                if (p.name == name && p.type == "blackbody")
                {
                    if (p.floats.size() != 2)
                        pbrt.ErrorExit(p.loc, "Expected two values for legacy \"blackbody\" parameter.");
                    scale *= p.floats[1];
                    p.floats.pop_back();
                }
            }
            return scale;
        }

        private void remove(string name, string typeName)
        {
            for (int i = 0; i < @params.Count; ++i)
                if (@params[i].name == name && @params[i].type == typeName)
                {
                    @params.RemoveAt(i);
                    return;
                }
        }

        public void RemoveFloat(string name) { remove(name, FloatTraits.typeName); }
        public void RemoveInt(string name) { remove(name, IntegerTraits.typeName); }
        public void RemoveBool(string name) { remove(name, BooleanTraits.typeName); }
        public void RemovePoint2f(string name) { remove(name, Point2fTraits.typeName); }
        public void RemoveVector2f(string name) { remove(name, Vector2fTraits.typeName); }
        public void RemovePoint3f(string name) { remove(name, Point3fTraits.typeName); }
        public void RemoveVector3f(string name) { remove(name, Vector3fTraits.typeName); }
        public void RemoveNormal3f(string name) { remove(name, Normal3fTraits.typeName); }
        public void RemoveString(string name) { remove(name, StringTraits.typeName); }
        public void RemoveTexture(string name) { remove(name, "texture"); }
        public void RemoveSpectrum(string name)
        {
            remove(name, "spectrum");
            remove(name, "rgb");
            remove(name, "blackbody");
        }

        public void RenameParameter(string before, string after)
        {
            foreach (ParsedParameter p in @params)
                if (p.name == before)
                    p.name = after;
        }

        public void RenameUsedTextures(SortedDictionary<string, string> m)
        {
            foreach (ParsedParameter p in @params)
            {
                if (p.type != "texture")
                    continue;
                pbrt.CHECK_EQ(1, (int)p.strings.size());
                if (m.TryGetValue(p.strings[0], out string renamed))
                    p.strings[0] = renamed;
            }
        }

        public void ReportUnused()
        {
            // type / name
            List<KeyValuePair<string, string>> seen = new List<KeyValuePair<string, string>>();

            foreach (ParsedParameter p in @params)
            {
                if (p.mayBeUnused)
                    continue;

                bool haveSeen = seen.Exists((KeyValuePair<string, string> p2) => p2.Key == p.type && p2.Value == p.name);

                if (p.lookedUp)
                {
                    // A parameter may be used when creating an initial Material, say,
                    // but then an override from a Shape may shadow it such that its
                    // name is already in the seen array.
                    if (!haveSeen)
                        seen.Add(new KeyValuePair<string, string>(p.type, p.name));
                }
                else if (haveSeen)
                {
                    // It's shadowed by another parameter; that's fine.
                }
                else
                    pbrt.ErrorExit(p.loc, pbrt.StringPrintf("\"%s\": unused parameter.", p.name));
            }
        }
        public string ToParameterDefinition(string name)
        {
            foreach (ParsedParameter p in @params)
                if (p.name == name)
                    return ToParameterDefinition(p, 0);
            return "";
        }

        // ParameterDictionary Private Methods
        internal T lookupSingle<T>(string name, T defaultValue, ParameterTypeTraits<T> traits)
        {
            // Search _params_ for parameter _name_
            foreach (ParsedParameter p in @params)
            {
                if (p.name != name || p.type != traits.typeName)
                    continue;

                // Extract parameter values from _p_
                ulong nvalues = traits.nValues(p);

                // Issue error if an incorrect number of parameter values were provided
                if (nvalues == 0)
                    pbrt.ErrorExit(p.loc, pbrt.StringPrintf("No values provided for parameter \"%s\".", name));
                if (nvalues != (ulong)traits.nPerItem)
                    pbrt.ErrorExit(p.loc, pbrt.StringPrintf("Expected %d values for parameter \"%s\".", traits.nPerItem, name));

                // Return parameter values as _ReturnType_
                p.lookedUp = true;
                return traits.convert(p, 0);
            }
            return defaultValue;
        }

        internal List<T> lookupArray<T>(string name, ParameterTypeTraits<T> traits)
        {
            foreach (ParsedParameter p in @params)
                if (p.name == name && p.type == traits.typeName)
                    return returnArray(traits.nValues(p), p, traits.nPerItem, traits.convert);
            return new List<T>();
        }

        private static List<T> returnArray<T>(ulong nValues, ParsedParameter param, int nPerItem, Func<ParsedParameter, int, T> convert)
        {
            if (nValues == 0)
                pbrt.ErrorExit(param.loc, pbrt.StringPrintf("No values provided for \"%s\".", param.name));
            if (nValues % (ulong)nPerItem != 0)
                pbrt.ErrorExit(param.loc, pbrt.StringPrintf("Number of values provided for \"%s\" not a multiple of %d", param.name, nPerItem));

            param.lookedUp = true;
            int n = (int)(nValues / (ulong)nPerItem);
            List<T> v = new List<T>(n);
            for (int i = 0; i < n; ++i)
                v.Add(convert(param, nPerItem * i));
            return v;
        }

        private static string ToParameterDefinition(ParsedParameter p, int indentCount)
        {
            string s = pbrt.StringPrintf("\"%s %s\" [ ", p.type, p.name);
            int continuationIndent = indentCount + 10 + p.type.Length + p.name.Length;
            int column = indentCount + 4 + s.Length;

            StringBuilder sb = new StringBuilder(s);
            void printOne(string val)
            {
                if (column > 80)
                {
                    sb.Append("\n");
                    sb.Append(' ', continuationIndent);
                    column = continuationIndent;
                }
                column += val.Length;
                sb.Append(val);
            }

            for (int i = 0; i < (int)p.floats.size(); ++i)
                printOne(pbrt.StringPrintf("%f ", p.floats[i]));
            for (int i = 0; i < (int)p.ints.size(); ++i)
                printOne(pbrt.StringPrintf("%i ", p.ints[i]));
            for (int i = 0; i < (int)p.strings.size(); ++i)
                printOne('"' + p.strings[i] + "\" ");
            for (int i = 0; i < (int)p.bools.size(); ++i)
                printOne(p.bools[i] != 0 ? "true " : "false ");
            sb.Append("]");
            return sb.ToString();
        }

        private static readonly SortedDictionary<string, Spectrum> cachedSpectra = new SortedDictionary<string, Spectrum>(StringComparer.Ordinal);

        // TODO: move this functionality (but not the caching?) to a Spectrum method.
        private static Spectrum readSpectrumFromFile(string filename, Allocator alloc)
        {
            string fn = pbrt.ResolveFilename(filename);
            if (cachedSpectra.TryGetValue(fn, out Spectrum cached))
                return cached;

            pstd.optional<Spectrum> pls = PiecewiseLinearSpectrum.Read(fn, alloc);
            if (!pls)
                return null;
            cachedSpectra[fn] = pls.value();
            return pls.value();
        }

        public string ToParameterList(int indent = 0)
        {
            string s = "";
            foreach (ParsedParameter p in @params)
            {
                s += new string(' ', indent + 4);
                s += ToParameterDefinition(p, indent);
                s += "\n";
            }
            return s;
        }
        public override string ToString()
        {
            string s = "[ ParameterDictionary params: ";
            foreach (ParsedParameter p in @params)
                s += "[ " + p.ToString() + "] ";
            s += pbrt.StringPrintf("colorSpace: %s ", colorSpace != null ? colorSpace.ToString() : "<nullptr>");
            s += "]";
            return s;
        }

        public FileLoc loc(string name)
        {
            foreach (ParsedParameter p in @params)
                if (p.name == name)
                    return p.loc;
            return null;
        }

        // ParameterDictionary Private Members
        internal List<ParsedParameter> @params = new List<ParsedParameter>();
        private RGBColorSpace colorSpace = null;
        private int nOwnedParams;
    }

    // TextureParameterDictionary Definition
    public sealed unsafe class TextureParameterDictionary
    {
        // TextureParameterDictionary Public Methods
        public TextureParameterDictionary(ParameterDictionary dict, NamedTextures textures)
        {
            this.dict = dict;
            this.textures = textures;
        }

        public static implicit operator ParameterDictionary(TextureParameterDictionary t) { return t.dict; }

        public Float GetOneFloat(string name, Float def) { return dict.GetOneFloat(name, def); }
        public int GetOneInt(string name, int def) { return dict.GetOneInt(name, def); }
        public bool GetOneBool(string name, bool def) { return dict.GetOneBool(name, def); }
        public Point2f GetOnePoint2f(string name, Point2f def) { return dict.GetOnePoint2f(name, def); }
        public Vector2f GetOneVector2f(string name, Vector2f def) { return dict.GetOneVector2f(name, def); }
        public Point3f GetOnePoint3f(string name, Point3f def) { return dict.GetOnePoint3f(name, def); }
        public Vector3f GetOneVector3f(string name, Vector3f def) { return dict.GetOneVector3f(name, def); }
        public Normal3f GetOneNormal3f(string name, Normal3f def) { return dict.GetOneNormal3f(name, def); }
        public Spectrum GetOneSpectrum(string name, Spectrum def, SpectrumType spectrumType, Allocator alloc)
        {
            return dict.GetOneSpectrum(name, def, spectrumType, alloc);
        }
        public string GetOneString(string name, string def) { return dict.GetOneString(name, def); }

        public List<Float> GetFloatArray(string name) { return dict.GetFloatArray(name); }
        public List<int> GetIntArray(string name) { return dict.GetIntArray(name); }
        public List<byte> GetBoolArray(string name) { return dict.GetBoolArray(name); }
        public List<Point2f> GetPoint2fArray(string name) { return dict.GetPoint2fArray(name); }
        public List<Vector2f> GetVector2fArray(string name) { return dict.GetVector2fArray(name); }
        public List<Point3f> GetPoint3fArray(string name) { return dict.GetPoint3fArray(name); }
        public List<Vector3f> GetVector3fArray(string name) { return dict.GetVector3fArray(name); }
        public List<Normal3f> GetNormal3fArray(string name) { return dict.GetNormal3fArray(name); }
        public List<Spectrum> GetSpectrumArray(string name, SpectrumType spectrumType, Allocator alloc)
        {
            return dict.GetSpectrumArray(name, spectrumType, alloc);
        }
        public List<string> GetStringArray(string name) { return dict.GetStringArray(name); }

        public SpectrumTexture GetSpectrumTexture(string name, Spectrum defaultValue, SpectrumType spectrumType, Allocator alloc)
        {
            SpectrumTexture tex = GetSpectrumTextureOrNull(name, spectrumType, alloc);
            if (tex != null)
                return tex;
            else if (defaultValue != null)
                return new SpectrumConstantTexture(defaultValue);
            else
                return null;
        }
        public SpectrumTexture GetSpectrumTextureOrNull(string name, SpectrumType spectrumType, Allocator alloc)
        {
            SortedDictionary<string, SpectrumTexture> spectrumTextures =
                (spectrumType == SpectrumType.Unbounded)
                    ? textures.unboundedSpectrumTextures
                    : ((spectrumType == SpectrumType.Albedo) ? textures.albedoSpectrumTextures : textures.illuminantSpectrumTextures);

            foreach (ParsedParameter p in dict.@params)
            {
                if (p.name != name)
                    continue;

                if (p.type == "texture")
                {
                    if (p.strings.empty())
                        pbrt.ErrorExit(p.loc, pbrt.StringPrintf("No texture name provided for parameter \"%s\".", name));
                    if (p.strings.size() != 1)
                        pbrt.ErrorExit(p.loc, pbrt.StringPrintf("More than one texture name provided for parameter \"%s\".", name));

                    p.lookedUp = true;

                    if (spectrumTextures.TryGetValue(p.strings[0], out SpectrumTexture st))
                        return st;

                    pbrt.ErrorExit(p.loc, pbrt.StringPrintf("Couldn't find spectrum texture named \"%s\" for parameter \"%s\"",
                                                                  p.strings[0], p.name));
                }
                else if (p.type == "rgb")
                {
                    if (p.floats.size() != 3)
                        pbrt.ErrorExit(p.loc, pbrt.StringPrintf("Didn't find three values for \"rgb\" parameter \"%s\".", p.name));
                    p.lookedUp = true;

                    RGB rgb = new RGB(p.floats[0], p.floats[1], p.floats[2]);
                    if (rgb.r < 0 || rgb.g < 0 || rgb.b < 0)
                        pbrt.ErrorExit(p.loc, pbrt.StringPrintf("Negative value provided for RGB parameter \"%s\".", p.name));

                    Spectrum s;
                    if (spectrumType == SpectrumType.Illuminant)
                        s = new RGBIlluminantSpectrum(dict.ColorSpace(), rgb);
                    else if (spectrumType == SpectrumType.Unbounded)
                        s = new RGBUnboundedSpectrum(dict.ColorSpace(), rgb);
                    else
                    {
                        pbrt.CHECK(spectrumType == SpectrumType.Albedo);
                        if (rgb.r > 1 || rgb.g > 1 || rgb.b > 1)
                            pbrt.ErrorExit(p.loc, pbrt.StringPrintf("RGB parameter \"%s\" used as an albedo has > 1 component.", p.name));
                        s = new RGBAlbedoSpectrum(dict.ColorSpace(), rgb);
                    }
                    return new SpectrumConstantTexture(s);
                }
                else if (p.type == "spectrum" || p.type == "blackbody")
                {
                    Spectrum s = GetOneSpectrum(name, null, spectrumType, alloc);
                    pbrt.CHECK(s != null);
                    return new SpectrumConstantTexture(s);
                }
            }

            return null;
        }

        public FloatTexture GetFloatTexture(string name, Float defaultValue, Allocator alloc)
        {
            FloatTexture tex = GetFloatTextureOrNull(name, alloc);
            return tex != null ? tex : new FloatConstantTexture(defaultValue);
        }
        public FloatTexture GetFloatTextureOrNull(string name, Allocator alloc)
        {
            foreach (ParsedParameter p in dict.@params)
            {
                if (p.name != name)
                    continue;

                if (p.type == "texture")
                {
                    if (p.strings.empty())
                        pbrt.ErrorExit(p.loc, pbrt.StringPrintf("No texture name provided for parameter \"%s\".", name));
                    if (p.strings.size() != 1)
                        pbrt.ErrorExit(p.loc, pbrt.StringPrintf("More than one texture name provided for parameter \"%s\".", name));

                    p.lookedUp = true;

                    if (textures.floatTextures.TryGetValue(p.strings[0], out FloatTexture ft))
                        return ft;

                    pbrt.ErrorExit(p.loc, pbrt.StringPrintf("Couldn't find float texture named \"%s\" for parameter \"%s\"",
                                                                  p.strings[0], p.name));
                }
                else if (p.type == "float")
                {
                    Float v = GetOneFloat(name, 0f);  // we know this will be found
                    return new FloatConstantTexture(v);
                }
            }

            return null;
        }

        public void ReportUnused() { dict.ReportUnused(); }

        // TextureParameterDictionary Private Members
        private ParameterDictionary dict;
        private NamedTextures textures;
    }

}  // namespace pbrt
