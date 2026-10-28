// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// parser.h / parser.cpp
//
// Notes: Token::token is a string rather than a string_view; the memory-mapped input path is not
// used (files are read into a string); double-conversion's StringToDouble is replaced by .NET
// number parsing (decimal, exponent and hexadecimal-integer forms).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace pbrt
{
    public static unsafe partial class pbrt
    {
        static string toString(string s) { return s; }

        static int parseInt(Token t)
        {
            bool negate = t.token[0] == '-';

            int index = 0;
            if (t.token[0] == '+' || t.token[0] == '-')
                ++index;

            int64_t value = 0;
            while (index < t.token.Length)
            {
                if (!(t.token[index] >= '0' && t.token[index] <= '9'))
                    ErrorExit(t.loc, "\"" + t.token[index] + "\": expected a number");
                value = 10 * value + (t.token[index] - '0');
                ++index;

                if (value > int.MaxValue)
                    ErrorExit(t.loc,
                              "Numeric value too large to represent as a 32-bit integer.");
                else if (value < int.MinValue)
                    Warning(t.loc, StringPrintf("Numeric value %d too low to represent as a 32-bit integer.", value));
            }

            return (int)(negate ? -value : value);
        }

        static double parseFloat(Token t)
        {
            // Fast path for a single digit
            if (t.token.Length == 1)
            {
                if (!(t.token[0] >= '0' && t.token[0] <= '9'))
                    ErrorExit(t.loc, "\"" + t.token[0] + "\": expected a number");
                return t.token[0] - '0';
            }

            // Can we just use strtol?
            Func<string, bool> isInteger = (string str) =>
            {
                foreach (char ch in str)
                    if (!(ch >= '0' && ch <= '9'))
                        return false;
                return true;
            };

            double val = 0;
            bool ok;
            if (isInteger(t.token))
            {
                ok = long.TryParse(t.token, NumberStyles.None, CultureInfo.InvariantCulture, out long lv);
                val = (double)lv;
            }
            else
            {
                string s = t.token;
                bool neg = false;
                string body = s;
                if (body.Length > 0 && (body[0] == '+' || body[0] == '-'))
                {
                    neg = body[0] == '-';
                    body = body.Substring(1);
                }
                if (body.Length > 2 && body[0] == '0' && (body[1] == 'x' || body[1] == 'X'))
                {
                    // ALLOW_HEX
                    ok = ulong.TryParse(body.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong hv);
                    val = neg ? -(double)hv : (double)hv;
                }
                else
                {
                    // Reject "Infinity", "NaN", etc.; only digits, '.', exponents and signs are valid.
                    ok = body.Length > 0;
                    foreach (char ch in body)
                        if (!((ch >= '0' && ch <= '9') || ch == '.' || ch == 'e' || ch == 'E' || ch == '+' || ch == '-'))
                            ok = false;
                    if (ok)
                    {
                        if (typeof(Float) == typeof(float))
                        {
                            ok = float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float fv);
                            val = fv;
                        }
                        else
                            ok = double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out val);
                    }
                }
            }

            if (!ok)
                ErrorExit(t.loc, StringPrintf("%s: expected a number", toString(t.token)));

            return val;
        }

        static bool isQuotedString(string str)
        {
            return str.Length >= 2 && str[0] == '"' && str[str.Length - 1] == '"';
        }

        static string dequoteString(Token t)
        {
            if (!isQuotedString(t.token))
                ErrorExit(t.loc, "\"" + toString(t.token) + "\": expected quoted string");

            return t.token.Substring(1, t.token.Length - 2);
        }

        const int TokenOptional = 0;
        const int TokenRequired = 1;

        static List<ParsedParameter> parseParameters(
            Func<int, Token> nextToken, Action<Token> ungetToken, bool formatting,
            Action<Token, string> errorCallback)
        {
            List<ParsedParameter> parameterVector = new List<ParsedParameter>();

            while (true)
            {
                Token t = nextToken(TokenOptional);
                if (t == null)
                    return parameterVector;

                if (!isQuotedString(t.token))
                {
                    ungetToken(t);
                    return parameterVector;
                }

                ParsedParameter param = new ParsedParameter(t.loc);

                string decl = dequoteString(t);

                Func<int, int> skipSpace = (int iter) =>
                {
                    while (iter != decl.Length && (decl[iter] == ' ' || decl[iter] == '\t'))
                        ++iter;
                    return iter;
                };
                // Skip to the next whitespace character (or the end of the string).
                Func<int, int> skipToSpace = (int iter) =>
                {
                    while (iter != decl.Length && decl[iter] != ' ' && decl[iter] != '\t')
                        ++iter;
                    return iter;
                };

                int typeBegin = skipSpace(0);
                if (typeBegin == decl.Length)
                    ErrorExit(t.loc, StringPrintf("Parameter \"%s\" doesn't have a type declaration?!", decl));

                // Find end of type declaration
                int typeEnd = skipToSpace(typeBegin);
                param.type = decl.Substring(typeBegin, typeEnd - typeBegin);

                if (formatting)
                {  // close enough: upgrade...
                    if (param.type == "point")
                        param.type = "point3";
                    if (param.type == "vector")
                        param.type = "vector3";
                    if (param.type == "color")
                        param.type = "rgb";
                }

                int nameBegin = skipSpace(typeEnd);
                if (nameBegin == decl.Length)
                    ErrorExit(t.loc, StringPrintf("Unable to find parameter name from \"%s\"", decl));

                int nameEnd = skipToSpace(nameBegin);
                param.name = decl.Substring(nameBegin, nameEnd - nameBegin);

                // enum ValType { Unknown, String, Bool, Float, Int }
                const int Unknown = 0, String = 1, Bool = 2, FloatT = 3, Int = 4;
                int valType = Unknown;

                if (param.type == "integer")
                    valType = Int;

                Action<Token> addVal = (Token tk) =>
                {
                    if (isQuotedString(tk.token))
                    {
                        switch (valType)
                        {
                            case Unknown:
                                valType = String;
                                break;
                            case String:
                                break;
                            case FloatT:
                                errorCallback(tk, "expected floating-point value");
                                break;
                            case Int:
                                errorCallback(tk, "expected integer value");
                                break;
                            case Bool:
                                errorCallback(tk, "expected Boolean value");
                                break;
                        }

                        param.AddString(dequoteString(tk));
                    }
                    else if (tk.token[0] == 't' && tk.token == "true")
                    {
                        switch (valType)
                        {
                            case Unknown:
                                valType = Bool;
                                break;
                            case String:
                                errorCallback(tk, "expected string value");
                                break;
                            case FloatT:
                                errorCallback(tk, "expected floating-point value");
                                break;
                            case Int:
                                errorCallback(tk, "expected integer value");
                                break;
                            case Bool:
                                break;
                        }

                        param.AddBool(true);
                    }
                    else if (tk.token[0] == 'f' && tk.token == "false")
                    {
                        switch (valType)
                        {
                            case Unknown:
                                valType = Bool;
                                break;
                            case String:
                                errorCallback(tk, "expected string value");
                                break;
                            case FloatT:
                                errorCallback(tk, "expected floating-point value");
                                break;
                            case Int:
                                errorCallback(tk, "expected integer value");
                                break;
                            case Bool:
                                break;
                        }

                        param.AddBool(false);
                    }
                    else
                    {
                        switch (valType)
                        {
                            case Unknown:
                                valType = FloatT;
                                break;
                            case String:
                                errorCallback(tk, "expected string value");
                                break;
                            case FloatT:
                                break;
                            case Int:
                                break;
                            case Bool:
                                errorCallback(tk, "expected Boolean value");
                                break;
                        }

                        if (valType == Int)
                            param.AddInt(parseInt(tk));
                        else
                            param.AddFloat((Float)parseFloat(tk));
                    }
                };

                Token val = nextToken(TokenRequired);

                if (val.token == "[")
                {
                    while (true)
                    {
                        val = nextToken(TokenRequired);
                        if (val.token == "]")
                            break;
                        addVal(val);
                    }
                }
                else
                {
                    addVal(val);
                }

                if (formatting && param.type == "bool")
                {
                    for (ulong i = 0; i < param.strings.size(); ++i)
                    {
                        string b = param.strings[i];
                        if (b == "true")
                            param.bools.push_back(1);
                        else if (b == "false")
                            param.bools.push_back(0);
                        else
                            Error(param.loc,
                                  StringPrintf("%s: neither \"true\" nor \"false\" in bool " +
                                               "parameter list.", b));
                    }
                    param.strings.clear();
                }

                parameterVector.Add(param);
            }
        }

        static bool warnedTransformBeginEndDeprecated = false;

        public static void parse(ParserTarget target, Tokenizer t)
        {
            FormattingParserTarget formattingTarget = target as FormattingParserTarget;
            bool formatting = formattingTarget != null;

            List<(AsyncJob<int>, BasicSceneBuilder)> imports = new List<(AsyncJob<int>, BasicSceneBuilder)>();

            LOG_VERBOSE("Started parsing %s", t.loc.filename);
            List<Tokenizer> fileStack = new List<Tokenizer>();
            fileStack.Add(t);

            Token ungetTokenV = null;

            Action<string, FileLoc> parseError = (string msg, FileLoc loc) =>
            {
                ErrorExit(loc, msg);
            };

            // nextToken is a little helper function that handles the file stack,
            // returning the next token from the current file until reaching EOF,
            // at which point it switches to the next file (if any).
            Func<int, Token> nextToken = null;
            nextToken = (int flags) =>
            {
                if (ungetTokenV != null)
                {
                    Token u = ungetTokenV;
                    ungetTokenV = null;
                    return u;
                }

                if (fileStack.Count == 0)
                {
                    if ((flags & TokenRequired) != 0)
                    {
                        ErrorExit("premature end of file");
                    }
                    return null;
                }

                Token tok = fileStack[fileStack.Count - 1].Next();

                if (tok == null)
                {
                    // We've reached EOF in the current file. Anything more to parse?
                    LOG_VERBOSE("Finished parsing %s", fileStack[fileStack.Count - 1].loc.filename);
                    fileStack.RemoveAt(fileStack.Count - 1);
                    return nextToken(flags);
                }
                else if (tok.token[0] == '#')
                {
                    // Swallow comments, unless --format or --toply was given, in
                    // which case they're printed to stdout.
                    if (formatting)
                        Console.Out.Write(StringPrintf("%s%s\n",
                                                       (target as FormattingParserTarget).indent(),
                                                       toString(tok.token)));
                    return nextToken(flags);
                }
                else
                    // Regular token; success.
                    return tok;
            };

            Action<Token> unget = (Token tk) =>
            {
                CHECK(ungetTokenV == null);
                ungetTokenV = tk;
            };

            // Helper function for pbrt API entrypoints that take a single string
            // parameter and a ParameterVector (e.g. pbrtShape()).
            Action<Action<string, List<ParsedParameter>, FileLoc>, FileLoc> basicParamListEntrypoint =
                (Action<string, List<ParsedParameter>, FileLoc> apiFunc, FileLoc loc) =>
                {
                    Token tk = nextToken(TokenRequired);
                    string dequoted = dequoteString(tk);
                    string n = toString(dequoted);
                    List<ParsedParameter> parameterVector = parseParameters(
                        nextToken, unget, formatting, (Token tt, string msg) =>
                        {
                            string token = toString(tt.token);
                            string str = StringPrintf("%s: %s", token, msg);
                            parseError(str, tt.loc);
                        });
                    apiFunc(n, parameterVector, loc);
                };

            Action<Token> syntaxError = (Token tk) =>
            {
                ErrorExit(tk.loc, "Unknown directive: " + toString(tk.token));
            };

            Token tok2 = null;

            while (true)
            {
                tok2 = nextToken(TokenOptional);
                if (tok2 == null)
                    break;

                switch (tok2.token[0])
                {
                    case 'A':
                        if (tok2.token == "AttributeBegin")
                            target.AttributeBegin(tok2.loc);
                        else if (tok2.token == "AttributeEnd")
                            target.AttributeEnd(tok2.loc);
                        else if (tok2.token == "Attribute")
                            basicParamListEntrypoint(target.Attribute, tok2.loc);
                        else if (tok2.token == "ActiveTransform")
                        {
                            Token a = nextToken(TokenRequired);
                            if (a.token == "All")
                                target.ActiveTransformAll(tok2.loc);
                            else if (a.token == "EndTime")
                                target.ActiveTransformEndTime(tok2.loc);
                            else if (a.token == "StartTime")
                                target.ActiveTransformStartTime(tok2.loc);
                            else
                                syntaxError(tok2);
                        }
                        else if (tok2.token == "AreaLightSource")
                            basicParamListEntrypoint(target.AreaLightSource, tok2.loc);
                        else if (tok2.token == "Accelerator")
                            basicParamListEntrypoint(target.Accelerator, tok2.loc);
                        else
                            syntaxError(tok2);
                        break;

                    case 'C':
                        if (tok2.token == "ConcatTransform")
                        {
                            if (nextToken(TokenRequired).token != "[")
                                syntaxError(tok2);
                            Float[] m = new Float[16];
                            for (int i = 0; i < 16; ++i)
                                m[i] = (Float)parseFloat(nextToken(TokenRequired));
                            if (nextToken(TokenRequired).token != "]")
                                syntaxError(tok2);
                            target.ConcatTransform(m, tok2.loc);
                        }
                        else if (tok2.token == "CoordinateSystem")
                        {
                            string n = dequoteString(nextToken(TokenRequired));
                            target.CoordinateSystem(toString(n), tok2.loc);
                        }
                        else if (tok2.token == "CoordSysTransform")
                        {
                            string n = dequoteString(nextToken(TokenRequired));
                            target.CoordSysTransform(toString(n), tok2.loc);
                        }
                        else if (tok2.token == "ColorSpace")
                        {
                            string n = dequoteString(nextToken(TokenRequired));
                            target.ColorSpace(toString(n), tok2.loc);
                        }
                        else if (tok2.token == "Camera")
                            basicParamListEntrypoint(target.Camera, tok2.loc);
                        else
                            syntaxError(tok2);
                        break;

                    case 'F':
                        if (tok2.token == "Film")
                            basicParamListEntrypoint(target.Film, tok2.loc);
                        else
                            syntaxError(tok2);
                        break;

                    case 'I':
                        if (tok2.token == "Integrator")
                            basicParamListEntrypoint(target.Integrator, tok2.loc);
                        else if (tok2.token == "Include")
                        {
                            Token filenameToken = nextToken(TokenRequired);
                            string filename = toString(dequoteString(filenameToken));
                            if (formatting)
                                Printf("%sInclude \"%s\"\n",
                                       (target as FormattingParserTarget).indent(),
                                       filename);
                            else
                            {
                                filename = ResolveFilename(filename);
                                Tokenizer tinc =
                                    Tokenizer.CreateFromFile(filename, parseError);
                                if (tinc != null)
                                {
                                    LOG_VERBOSE("Started parsing %s", tinc.loc.filename);
                                    fileStack.Add(tinc);
                                }
                            }
                        }
                        else if (tok2.token == "Import")
                        {
                            Token filenameToken = nextToken(TokenRequired);
                            string filename = toString(dequoteString(filenameToken));
                            if (formatting)
                                Printf("%sImport \"%s\"\n",
                                       (target as FormattingParserTarget).indent(),
                                       filename);
                            else
                            {
                                BasicSceneBuilder builder = target as BasicSceneBuilder;
                                CHECK(builder != null);

                                if (builder.currentBlock !=
                                    BasicSceneBuilder.BlockState.WorldBlock)
                                    ErrorExit(tok2.loc, "Import statement only allowed inside world " +
                                                        "definition block.");

                                filename = ResolveFilename(filename);
                                BasicSceneBuilder importBuilder = builder.CopyForImport();

                                if (RunningThreads() == 1)
                                {
                                    Tokenizer timport =
                                        Tokenizer.CreateFromFile(filename, parseError);
                                    if (timport != null)
                                        parse(importBuilder, timport);
                                    builder.MergeImported(importBuilder);
                                }
                                else
                                {
                                    string fnCopy = filename;
                                    Func<int> job = () =>
                                    {
                                        Timer timer = new Timer();
                                        Tokenizer timport =
                                            Tokenizer.CreateFromFile(fnCopy, parseError);
                                        if (timport != null)
                                            parse(importBuilder, timport);
                                        LOG_VERBOSE("Elapsed time to parse \"%s\": %.2fs", fnCopy,
                                                    timer.ElapsedSeconds());
                                        return 0;
                                    };
                                    AsyncJob<int> jobFinished = RunAsync(job);
                                    imports.Add((jobFinished, importBuilder));
                                }
                            }
                        }
                        else if (tok2.token == "Identity")
                            target.Identity(tok2.loc);
                        else
                            syntaxError(tok2);
                        break;

                    case 'L':
                        if (tok2.token == "LightSource")
                            basicParamListEntrypoint(target.LightSource, tok2.loc);
                        else if (tok2.token == "LookAt")
                        {
                            Float[] v = new Float[9];
                            for (int i = 0; i < 9; ++i)
                                v[i] = (Float)parseFloat(nextToken(TokenRequired));
                            target.LookAt(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8],
                                          tok2.loc);
                        }
                        else
                            syntaxError(tok2);
                        break;

                    case 'M':
                        if (tok2.token == "MakeNamedMaterial")
                            basicParamListEntrypoint(target.MakeNamedMaterial, tok2.loc);
                        else if (tok2.token == "MakeNamedMedium")
                            basicParamListEntrypoint(target.MakeNamedMedium, tok2.loc);
                        else if (tok2.token == "Material")
                            basicParamListEntrypoint(target.Material, tok2.loc);
                        else if (tok2.token == "MediumInterface")
                        {
                            string n = dequoteString(nextToken(TokenRequired));
                            string[] names = new string[2];
                            names[0] = toString(n);

                            // Check for optional second parameter
                            Token second = nextToken(TokenOptional);
                            if (second != null)
                            {
                                if (isQuotedString(second.token))
                                    names[1] = toString(dequoteString(second));
                                else
                                {
                                    unget(second);
                                    names[1] = names[0];
                                }
                            }
                            else
                                names[1] = names[0];

                            target.MediumInterface(names[0], names[1], tok2.loc);
                        }
                        else
                            syntaxError(tok2);
                        break;

                    case 'N':
                        if (tok2.token == "NamedMaterial")
                        {
                            string n = dequoteString(nextToken(TokenRequired));
                            target.NamedMaterial(toString(n), tok2.loc);
                        }
                        else
                            syntaxError(tok2);
                        break;

                    case 'O':
                        if (tok2.token == "ObjectBegin")
                        {
                            string n = dequoteString(nextToken(TokenRequired));
                            target.ObjectBegin(toString(n), tok2.loc);
                        }
                        else if (tok2.token == "ObjectEnd")
                            target.ObjectEnd(tok2.loc);
                        else if (tok2.token == "ObjectInstance")
                        {
                            string n = dequoteString(nextToken(TokenRequired));
                            target.ObjectInstance(toString(n), tok2.loc);
                        }
                        else if (tok2.token == "Option")
                        {
                            string name = toString(dequoteString(nextToken(TokenRequired)));
                            string value = toString(nextToken(TokenRequired).token);
                            target.Option(name, value, tok2.loc);
                        }
                        else
                            syntaxError(tok2);
                        break;

                    case 'P':
                        if (tok2.token == "PixelFilter")
                            basicParamListEntrypoint(target.PixelFilter, tok2.loc);
                        else
                            syntaxError(tok2);
                        break;

                    case 'R':
                        if (tok2.token == "ReverseOrientation")
                            target.ReverseOrientation(tok2.loc);
                        else if (tok2.token == "Rotate")
                        {
                            Float[] v = new Float[4];
                            for (int i = 0; i < 4; ++i)
                                v[i] = (Float)parseFloat(nextToken(TokenRequired));
                            target.Rotate(v[0], v[1], v[2], v[3], tok2.loc);
                        }
                        else
                            syntaxError(tok2);
                        break;

                    case 'S':
                        if (tok2.token == "Shape")
                            basicParamListEntrypoint(target.Shape, tok2.loc);
                        else if (tok2.token == "Sampler")
                            basicParamListEntrypoint(target.Sampler, tok2.loc);
                        else if (tok2.token == "Scale")
                        {
                            Float[] v = new Float[3];
                            for (int i = 0; i < 3; ++i)
                                v[i] = (Float)parseFloat(nextToken(TokenRequired));
                            target.Scale(v[0], v[1], v[2], tok2.loc);
                        }
                        else
                            syntaxError(tok2);
                        break;

                    case 'T':
                        if (tok2.token == "TransformBegin")
                        {
                            if (formattingTarget != null)
                                formattingTarget.TransformBegin(tok2.loc);
                            else
                            {
                                if (!warnedTransformBeginEndDeprecated)
                                {
                                    Warning(tok2.loc, "TransformBegin/End are deprecated and should " +
                                                      "be replaced with AttributeBegin/End");
                                    warnedTransformBeginEndDeprecated = true;
                                }
                                target.AttributeBegin(tok2.loc);
                            }
                        }
                        else if (tok2.token == "TransformEnd")
                        {
                            if (formattingTarget != null)
                                formattingTarget.TransformEnd(tok2.loc);
                            else
                                target.AttributeEnd(tok2.loc);
                        }
                        else if (tok2.token == "Transform")
                        {
                            if (nextToken(TokenRequired).token != "[")
                                syntaxError(tok2);
                            Float[] m = new Float[16];
                            for (int i = 0; i < 16; ++i)
                                m[i] = (Float)parseFloat(nextToken(TokenRequired));
                            if (nextToken(TokenRequired).token != "]")
                                syntaxError(tok2);
                            target.Transform(m, tok2.loc);
                        }
                        else if (tok2.token == "Translate")
                        {
                            Float[] v = new Float[3];
                            for (int i = 0; i < 3; ++i)
                                v[i] = (Float)parseFloat(nextToken(TokenRequired));
                            target.Translate(v[0], v[1], v[2], tok2.loc);
                        }
                        else if (tok2.token == "TransformTimes")
                        {
                            Float[] v = new Float[2];
                            for (int i = 0; i < 2; ++i)
                                v[i] = (Float)parseFloat(nextToken(TokenRequired));
                            target.TransformTimes(v[0], v[1], tok2.loc);
                        }
                        else if (tok2.token == "Texture")
                        {
                            string n = dequoteString(nextToken(TokenRequired));
                            string name = toString(n);
                            n = dequoteString(nextToken(TokenRequired));
                            string type = toString(n);

                            Token tk = nextToken(TokenRequired);
                            string dequoted = dequoteString(tk);
                            string texName = toString(dequoted);
                            List<ParsedParameter> parms = parseParameters(
                                nextToken, unget, formatting, (Token tt, string msg) =>
                                {
                                    string token = toString(tt.token);
                                    string str = StringPrintf("%s: %s", token, msg);
                                    parseError(str, tt.loc);
                                });

                            target.Texture(name, type, texName, parms, tok2.loc);
                        }
                        else
                            syntaxError(tok2);
                        break;

                    case 'W':
                        if (tok2.token == "WorldBegin")
                            target.WorldBegin(tok2.loc);
                        else if (tok2.token == "WorldEnd" && formatting)
                        { }  // just swallow it
                        else
                            syntaxError(tok2);
                        break;

                    default:
                        syntaxError(tok2);
                        break;
                }
            }

            foreach ((AsyncJob<int> job, BasicSceneBuilder import) in imports)
            {
                job.Wait();

                BasicSceneBuilder builder = target as BasicSceneBuilder;
                CHECK(builder != null);
                builder.MergeImported(import);
                // HACK: let import.second leak so that its TransformCache isn't deallocated...
            }
        }

        public static void ParseFiles(ParserTarget target, pstd.span<string> filenames)
        {
            Action<string, FileLoc> tokError = (string msg, FileLoc loc) =>
            {
                ErrorExit(loc, msg);
            };

            // Process scene description
            if (filenames.empty())
            {
                // Parse scene from standard input
                Tokenizer t = Tokenizer.CreateFromFile("-", tokError);
                if (t != null)
                    parse(target, t);
            }
            else
            {
                // Parse scene from input files
                for (int i = 0; i < (int)filenames.size(); ++i)
                {
                    string fn = filenames[i];
                    if (fn != "-")
                        SetSearchDirectory(fn);

                    Tokenizer t = Tokenizer.CreateFromFile(fn, tokError);
                    if (t != null)
                        parse(target, t);
                }
            }

            target.EndOfFiles();
        }

        public static void ParseString(ParserTarget target, string str)
        {
            Action<string, FileLoc> tokError = (string msg, FileLoc loc) =>
            {
                ErrorExit(loc, msg);
            };
            Tokenizer t = Tokenizer.CreateFromString(str, tokError);
            if (t == null)
                return;
            parse(target, t);

            target.EndOfFiles();
        }
    }


    ///////////////////////////////////////////////////////////////////////////
    // ParsedParameter

    public sealed unsafe partial class ParsedParameter
    {
        public void AddFloat(Float v)
        {
            CHECK(ints.empty() && strings.empty() && bools.empty());
            floats.push_back(v);
        }

        public void AddInt(int i)
        {
            CHECK(floats.empty() && strings.empty() && bools.empty());
            ints.push_back(i);
        }

        public void AddString(string str)
        {
            CHECK(floats.empty() && ints.empty() && bools.empty());
            strings.push_back(str);
        }

        public void AddBool(bool v)
        {
            CHECK(floats.empty() && ints.empty() && strings.empty());
            bools.push_back((byte)(v ? 1 : 0));
        }

        public override string ToString()
        {
            string str = "";
            str += "\"" + type + " " + name + "\" [ ";
            if (!floats.empty())
                for (ulong i = 0; i < floats.size(); ++i)
                    str += StringPrintf("%f ", floats[i]);
            else if (!ints.empty())
                for (ulong i = 0; i < ints.size(); ++i)
                    str += StringPrintf("%d ", ints[i]);
            else if (!strings.empty())
                for (ulong i = 0; i < strings.size(); ++i)
                    str += '\"' + strings[i] + "\" ";
            else if (!bools.empty())
                for (ulong i = 0; i < bools.size(); ++i)
                    str += bools[i] != 0 ? "true " : "false ";
            str += "] ";

            return str;
        }
    }

    // ParserTarget Definition
    public abstract unsafe class ParserTarget
    {
        // ParserTarget Interface
        public abstract void Scale(Float sx, Float sy, Float sz, FileLoc loc);

        public abstract void Shape(string name, List<ParsedParameter> parameters, FileLoc loc);

        public abstract void Option(string name, string value, FileLoc loc);

        public abstract void Identity(FileLoc loc);
        public abstract void Translate(Float dx, Float dy, Float dz, FileLoc loc);
        public abstract void Rotate(Float angle, Float ax, Float ay, Float az, FileLoc loc);
        public abstract void LookAt(Float ex, Float ey, Float ez, Float lx, Float ly, Float lz,
                                    Float ux, Float uy, Float uz, FileLoc loc);
        public abstract void ConcatTransform(Float[] transform, FileLoc loc);
        public abstract void Transform(Float[] transform, FileLoc loc);
        public abstract void CoordinateSystem(string name, FileLoc loc);
        public abstract void CoordSysTransform(string name, FileLoc loc);
        public abstract void ActiveTransformAll(FileLoc loc);
        public abstract void ActiveTransformEndTime(FileLoc loc);
        public abstract void ActiveTransformStartTime(FileLoc loc);
        public abstract void TransformTimes(Float start, Float end, FileLoc loc);

        public abstract void ColorSpace(string n, FileLoc loc);
        public abstract void PixelFilter(string name, List<ParsedParameter> parameters, FileLoc loc);
        public abstract void Film(string type, List<ParsedParameter> parameters, FileLoc loc);
        public abstract void Accelerator(string name, List<ParsedParameter> parameters, FileLoc loc);
        public abstract void Integrator(string name, List<ParsedParameter> parameters, FileLoc loc);
        public abstract void Camera(string name, List<ParsedParameter> parameters, FileLoc loc);
        public abstract void MakeNamedMedium(string name, List<ParsedParameter> parameters, FileLoc loc);
        public abstract void MediumInterface(string insideName, string outsideName, FileLoc loc);
        public abstract void Sampler(string name, List<ParsedParameter> parameters, FileLoc loc);

        public abstract void WorldBegin(FileLoc loc);
        public abstract void AttributeBegin(FileLoc loc);
        public abstract void AttributeEnd(FileLoc loc);
        public abstract void Attribute(string target, List<ParsedParameter> parameters, FileLoc loc);
        public abstract void Texture(string name, string type, string texname, List<ParsedParameter> parameters,
                                     FileLoc loc);
        public abstract void Material(string name, List<ParsedParameter> parameters, FileLoc loc);
        public abstract void MakeNamedMaterial(string name, List<ParsedParameter> parameters, FileLoc loc);
        public abstract void NamedMaterial(string name, FileLoc loc);
        public abstract void LightSource(string name, List<ParsedParameter> parameters, FileLoc loc);
        public abstract void AreaLightSource(string name, List<ParsedParameter> parameters, FileLoc loc);
        public abstract void ReverseOrientation(FileLoc loc);
        public abstract void ObjectBegin(string name, FileLoc loc);
        public abstract void ObjectEnd(FileLoc loc);
        public abstract void ObjectInstance(string name, FileLoc loc);

        public abstract void EndOfFiles();

        // ParserTarget Protected Methods
        protected void ErrorExitDeferred(string message)
        {
            errorExit = true;
            pbrt.Error(message);
        }
        protected void ErrorExitDeferred(FileLoc loc, string message)
        {
            errorExit = true;
            pbrt.Error(loc, message);
        }

        protected bool errorExit = false;
    }

    // Token Definition
    public sealed unsafe class Token
    {
        public Token() { }
        public Token(string token, FileLoc loc) { this.token = token; this.loc = loc; }
        public override string ToString()
        {
            return pbrt.StringPrintf("[ Token token: %s loc: %s ]", token, loc);
        }
        public string token;
        public FileLoc loc;
    }

    // Tokenizer Definition
    public sealed unsafe class Tokenizer
    {
        // STAT_MEMORY_COUNTER("Memory/Tokenizer buffers", tokenizerMemory);
        [ThreadStatic] static long tokenizerMemory;
        static StatRegisterer STATS_REG = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportMemoryCounter("Memory/Tokenizer buffers", tokenizerMemory);
            tokenizerMemory = 0;
        });

        const int EOF = -1;

        // Tokenizer Implementation
        static char decodeEscaped(int ch, FileLoc loc)
        {
            switch (ch)
            {
                case EOF:
                    pbrt.ErrorExit(loc, "premature EOF after character escape '\\'");
                    break;
                case 'b':
                    return '\b';
                case 'f':
                    return '\f';
                case 'n':
                    return '\n';
                case 'r':
                    return '\r';
                case 't':
                    return '\t';
                case '\\':
                    return '\\';
                case '\'':
                    return '\'';
                case '\"':
                    return '\"';
                default:
                    pbrt.ErrorExit(loc, "unexpected escaped character \"" + (char)ch + "\"");
                    break;
            }
            return (char)0;  // NOTREACHED
        }

        int getChar()
        {
            if (pos == end)
                return EOF;
            int ch = contents[pos++];
            if (ch == '\n')
            {
                ++loc.line;
                loc.column = 0;
            }
            else
                ++loc.column;
            return ch;
        }

        void ungetChar()
        {
            --pos;
            if (contents[pos] == '\n')
                // Don't worry about the column; we'll be going to the start of
                // the next line again shortly...
                --loc.line;
        }

        public static Tokenizer CreateFromFile(string filename, Action<string, FileLoc> errorCallback)
        {
            if (filename == "-")
            {
                // Handle stdin by slurping everything into a string.
                string str = Console.In.ReadToEnd();
                return new Tokenizer(str, "<stdin>", errorCallback);
            }

            if (pbrt.HasExtension(filename, ".gz"))
            {
                string str = pbrt.ReadDecompressedFileContents(filename);
                return new Tokenizer(str, filename, errorCallback);
            }

            if (!File.Exists(filename))
            {
                errorCallback(pbrt.StringPrintf("%s: %s", filename, "No such file or directory"), null);
                return null;
            }
            byte[] bytes = pbrt.ReadFileBytes(filename);
            Tokenizer t = new Tokenizer(Encoding.UTF8.GetString(bytes), filename, errorCallback);
            t.CheckUTF(bytes);
            return t;
        }

        public static Tokenizer CreateFromString(string str, Action<string, FileLoc> errorCallback)
        {
            return new Tokenizer(str, "<stdin>", errorCallback);
        }

        static FileLoc Copy(FileLoc l)
        {
            FileLoc c = new FileLoc(l.filename);
            c.line = l.line;
            c.column = l.column;
            return c;
        }

        public Tokenizer(string str, string filename, Action<string, FileLoc> errorCallback)
        {
            this.errorCallback = errorCallback;
            this.contents = str;
            loc = new FileLoc(filename);
            pos = 0;
            end = contents.Length;
            tokenizerMemory += contents.Length;
            if (contents.Length >= 1 && (contents[0] == '﻿' || contents[0] == '￾'))
                CheckUTF(new byte[] { (byte)(contents[0] == '﻿' ? 0xfe : 0xff), (byte)(contents[0] == '﻿' ? 0xff : 0xfe) });
        }

        void CheckUTF(byte[] c)
        {
            int len = c.Length;
            // https://en.wikipedia.org/wiki/Byte_order_mark
            if (len >= 2 && ((c[0] == 0xfe && c[1] == 0xff) || (c[0] == 0xff && c[1] == 0xfe)))
                errorCallback("File is encoded with UTF-16, which is not currently " +
                              "supported by pbrt (https://github.com/mmp/pbrt-v4/issues/136).",
                              loc);
        }

        public Token Next()
        {
            while (true)
            {
                int tokenStart = pos;
                FileLoc startLoc = Copy(loc);

                int ch = getChar();
                if (ch == EOF)
                    return null;
                else if (ch == ' ' || ch == '\n' || ch == '\t' || ch == '\r')
                {
                    // nothing
                }
                else if (ch == '"')
                {
                    // scan to closing quote
                    bool haveEscaped = false;
                    while ((ch = getChar()) != '"')
                    {
                        if (ch == EOF)
                        {
                            errorCallback("premature EOF", startLoc);
                            return null;
                        }
                        else if (ch == '\n')
                        {
                            errorCallback("unterminated string", startLoc);
                            return null;
                        }
                        else if (ch == '\\')
                        {
                            haveEscaped = true;
                            // Grab the next character
                            if ((ch = getChar()) == EOF)
                            {
                                errorCallback("premature EOF", startLoc);
                                return null;
                            }
                        }
                    }

                    if (!haveEscaped)
                        return new Token(contents.Substring(tokenStart, pos - tokenStart), startLoc);
                    else
                    {
                        StringBuilder sEscaped = new StringBuilder();
                        for (int p = tokenStart; p < pos; ++p)
                        {
                            if (contents[p] != '\\')
                                sEscaped.Append(contents[p]);
                            else
                            {
                                ++p;
                                CHECK_LT(p, pos);
                                sEscaped.Append(decodeEscaped(contents[p], startLoc));
                            }
                        }
                        return new Token(sEscaped.ToString(), startLoc);
                    }
                }
                else if (ch == '[' || ch == ']')
                {
                    return new Token(contents.Substring(tokenStart, 1), startLoc);
                }
                else if (ch == '#')
                {
                    // comment: scan to EOL (or EOF)
                    while ((ch = getChar()) != EOF)
                    {
                        if (ch == '\n' || ch == '\r')
                        {
                            ungetChar();
                            break;
                        }
                    }

                    return new Token(contents.Substring(tokenStart, pos - tokenStart), startLoc);
                }
                else
                {
                    // Regular statement or numeric token; scan until we hit a
                    // space, opening quote, or bracket.
                    while ((ch = getChar()) != EOF)
                    {
                        if (ch == ' ' || ch == '\n' || ch == '\t' || ch == '\r' || ch == '"' ||
                            ch == '[' || ch == ']')
                        {
                            ungetChar();
                            break;
                        }
                    }
                    return new Token(contents.Substring(tokenStart, pos - tokenStart), startLoc);
                }
            }
        }

        // Just for parse().
        public FileLoc loc;

        // Tokenizer Private Members
        // This function is called if there is an error during lexing.
        Action<string, FileLoc> errorCallback;

        string contents;

        // Current position in the file and one past the end of the file.
        int pos, end;
    }

    // FormattingParserTarget Definition
    public sealed unsafe class FormattingParserTarget : ParserTarget
    {
        public FormattingParserTarget(bool toPly, bool upgrade)
        {
            this.toPly = toPly;
            this.upgrade = upgrade;
        }

        // (destructor: exits if any deferred error occurred)
        public void Finish()
        {
            if (errorExit)
                ErrorExit("Fatal errors during scene updating.");
        }

        public string indent(int extra = 0)
        {
            return new string(' ', catIndentCount + 4 * extra);
        }

        // FormattingParserTarget Method Definitions
        public override void Option(string name, string value, FileLoc loc)
        {
            string nName = normalizeArg(name);
            Printf("%sOption \"%s\" %s\n", indent(), name, value);
        }

        public override void Identity(FileLoc loc)
        {
            Printf("%sIdentity\n", indent());
        }

        public override void Translate(Float dx, Float dy, Float dz, FileLoc loc)
        {
            Printf("%sTranslate %f %f %f\n", indent(), dx, dy, dz);
        }

        public override void Rotate(Float angle, Float ax, Float ay, Float az, FileLoc loc)
        {
            Printf("%sRotate %f %f %f %f\n", indent(), angle, ax, ay, az);
        }

        public override void Scale(Float sx, Float sy, Float sz, FileLoc loc)
        {
            Printf("%sScale %f %f %f\n", indent(), sx, sy, sz);
        }

        public override void LookAt(Float ex, Float ey, Float ez, Float lx, Float ly,
                                    Float lz, Float ux, Float uy, Float uz, FileLoc loc)
        {
            Printf("%sLookAt %f %f %f\n%s    %f %f %f\n%s    %f %f %f\n", indent(), ex, ey, ez,
                   indent(), lx, ly, lz, indent(), ux, uy, uz);
        }

        public override void ConcatTransform(Float[] transform, FileLoc loc)
        {
            Printf("%sConcatTransform [ ", indent());
            for (int i = 0; i < 16; ++i)
                Printf("%f ", transform[i]);
            Printf(" ]\n");
        }

        public override void Transform(Float[] transform, FileLoc loc)
        {
            Printf("%sTransform [ ", indent());
            for (int i = 0; i < 16; ++i)
                Printf("%f ", transform[i]);
            Printf(" ]\n");
        }

        public override void CoordinateSystem(string name, FileLoc loc)
        {
            Printf("%sCoordinateSystem \"%s\"\n", indent(), name);
        }

        public override void CoordSysTransform(string name, FileLoc loc)
        {
            Printf("%sCoordSysTransform \"%s\"\n", indent(), name);
        }

        public override void ActiveTransformAll(FileLoc loc)
        {
            Printf("%sActiveTransform All\n", indent());
        }

        public override void ActiveTransformEndTime(FileLoc loc)
        {
            Printf("%sActiveTransform EndTime\n", indent());
        }

        public override void ActiveTransformStartTime(FileLoc loc)
        {
            Printf("%sActiveTransform StartTime\n", indent());
        }

        public override void TransformTimes(Float start, Float end, FileLoc loc)
        {
            Printf("%sTransformTimes %f %f\n", indent(), start, end);
        }

        public override void ColorSpace(string n, FileLoc loc)
        {
            Printf("%sColorSpace \"%s\"\n", indent(), n);
        }

        public override void PixelFilter(string name, List<ParsedParameter> parameters, FileLoc loc)
        {
            ParameterDictionary dict = new ParameterDictionary(parameters, RGBColorSpace.sRGB);

            string extra = "";
            if (upgrade)
            {
                List<Float> xr = dict.GetFloatArray("xwidth");
                if (xr.Count == 1)
                {
                    dict.RemoveFloat("xwidth");
                    extra += StringPrintf("%s\"float xradius\" [ %f ]\n", indent(1), xr[0]);
                }
                List<Float> yr = dict.GetFloatArray("ywidth");
                if (yr.Count == 1)
                {
                    dict.RemoveFloat("ywidth");
                    extra += StringPrintf("%s\"float yradius\" [ %f ]\n", indent(1), yr[0]);
                }

                if (name == "gaussian")
                {
                    List<Float> alpha = dict.GetFloatArray("alpha");
                    if (alpha.Count == 1)
                    {
                        dict.RemoveFloat("alpha");
                        extra += StringPrintf("%s\"float sigma\" [ %f ]\n", indent(1),
                                              1 / std.sqrt(2 * alpha[0]));
                    }
                }
            }

            Printf("%sPixelFilter \"%s\"\n", indent(), name);
            Console.Out.Write(extra + dict.ToParameterList(catIndentCount));
        }

        public override void Film(string type, List<ParsedParameter> parameters, FileLoc loc)
        {
            ParameterDictionary dict = new ParameterDictionary(parameters, RGBColorSpace.sRGB);

            string extra = "";
            if (upgrade)
            {
                List<Float> m = dict.GetFloatArray("maxsampleluminance");
                if (m.Count != 0)
                {
                    dict.RemoveFloat("maxsampleluminance");
                    extra +=
                        StringPrintf("%s\"float maxcomponentvalue\" [ %f ]\n", indent(1), m[0]);
                }
                List<Float> s = dict.GetFloatArray("scale");
                if (s.Count != 0)
                {
                    dict.RemoveFloat("scale");
                    extra += StringPrintf("%s\"float iso\" [ %f ]\n", indent(1), 100 * s[0]);
                }
            }

            if (upgrade && type == "image")
                Printf("%sFilm \"rgb\"\n", indent());
            else
                Printf("%sFilm \"%s\"\n", indent(), type);
            Console.Out.Write(extra + dict.ToParameterList(catIndentCount));
        }

        public override void Sampler(string name, List<ParsedParameter> parameters, FileLoc loc)
        {
            ParameterDictionary dict = new ParameterDictionary(parameters, RGBColorSpace.sRGB);

            if (upgrade)
            {
                if (name == "lowdiscrepancy" || name == "02sequence")
                    Printf("%sSampler \"paddedsobol\"\n", indent());
                else if (name == "maxmindist")
                    Printf("%sSampler \"pmj02bn\"\n", indent());
                else if (name == "random")
                    Printf("%sSampler \"independent\"\n", indent());
                else
                    Printf("%sSampler \"%s\"\n", indent(), name);
            }
            else
                Printf("%sSampler \"%s\"\n", indent(), name);
            Console.Out.Write(dict.ToParameterList(catIndentCount));
        }

        public override void Accelerator(string name, List<ParsedParameter> parameters, FileLoc loc)
        {
            ParameterDictionary dict = new ParameterDictionary(parameters, RGBColorSpace.sRGB);

            Printf("%sAccelerator \"%s\"\n%s", indent(), name,
                   dict.ToParameterList(catIndentCount));
        }

        public override void Integrator(string name, List<ParsedParameter> parameters, FileLoc loc)
        {
            ParameterDictionary dict = new ParameterDictionary(parameters, RGBColorSpace.sRGB);

            string extra = "";
            if (upgrade)
            {
                dict.RemoveFloat("rrthreshold");

                if (name == "sppm")
                {
                    dict.RemoveInt("imagewritefrequency");

                    List<int> iterations = dict.GetIntArray("numiterations");
                    if (iterations.Count != 0)
                    {
                        dict.RemoveInt("numiterations");
                        Warning(
                            loc,
                            "The SPPM integrator no longer takes a \"numiterations\" parameter. " +
                            "This value is now set via the Sampler's number of pixel samples.");
                    }
                }
                string lss = dict.GetOneString("lightsamplestrategy", "");
                if (lss == "spatial")
                {
                    dict.RemoveString("lightsamplestrategy");
                    extra += indent(1) + "\"string lightsamplestrategy\" \"bvh\"\n";
                }
            }

            if (upgrade && name == "directlighting")
            {
                Printf("%sIntegrator \"path\"\n", indent());
                extra += indent(1) + "\"integer maxdepth\" [ 1 ]\n";
            }
            else
                Printf("%sIntegrator \"%s\"\n", indent(), name);
            Console.Out.Write(extra + dict.ToParameterList(catIndentCount));
        }

        public override void Camera(string name, List<ParsedParameter> parameters, FileLoc loc)
        {
            ParameterDictionary dict = new ParameterDictionary(parameters, RGBColorSpace.sRGB);

            if (upgrade && name == "environment")
                Printf("%sCamera \"spherical\" \"string mapping\" \"equirectangular\"\n",
                       indent());
            else
                Printf("%sCamera \"%s\"\n", indent(), name);
            if (upgrade && name == "realistic")
                dict.RemoveBool("simpleweighting");

            Console.Out.Write(dict.ToParameterList(catIndentCount));
        }

        public override void MakeNamedMedium(string name, List<ParsedParameter> parameters, FileLoc loc)
        {
            ParameterDictionary dict = new ParameterDictionary(parameters, RGBColorSpace.sRGB);
            if (upgrade && name == "heterogeneous")
                Printf("%sMakeNamedMedium \"%s\"\n%s\n", indent(), "uniformgrid",
                       dict.ToParameterList(catIndentCount));
            else
                Printf("%sMakeNamedMedium \"%s\"\n%s\n", indent(), name,
                       dict.ToParameterList(catIndentCount));
        }

        public override void MediumInterface(string insideName, string outsideName, FileLoc loc)
        {
            Printf("%sMediumInterface \"%s\" \"%s\"\n", indent(), insideName, outsideName);
        }

        public override void WorldBegin(FileLoc loc)
        {
            Printf("\n\nWorldBegin\n\n");
        }

        public override void AttributeBegin(FileLoc loc)
        {
            Printf("\n%sAttributeBegin\n", indent());
            catIndentCount += 4;
        }

        public override void AttributeEnd(FileLoc loc)
        {
            catIndentCount -= 4;
            Printf("%sAttributeEnd\n", indent());
        }

        public override void Attribute(string target, List<ParsedParameter> parameters, FileLoc loc)
        {
            ParameterDictionary dict = new ParameterDictionary(parameters, RGBColorSpace.sRGB);
            Printf("%sAttribute \"%s\" ", indent(), target);
            if (parameters.Count == 1)
                // just one; put it on the same line
                Console.Out.Write(dict.ToParameterList(0) + '\n');
            else
                Console.Out.Write('\n' + dict.ToParameterList(catIndentCount));
        }

        public void TransformBegin(FileLoc loc)
        {
            Warning(loc, "Rewriting \"TransformBegin\" to \"AttributeBegin\".");
            Printf("%sAttributeBegin\n", indent());
            catIndentCount += 4;
        }

        public void TransformEnd(FileLoc loc)
        {
            catIndentCount -= 4;
            Warning(loc, "Rewriting \"TransformEnd\" to \"AttributeEnd\".");
            Printf("%sAttributeEnd\n", indent());
        }

        static int textureCount = 0;

        public override void Texture(string name, string type, string texname,
                                     List<ParsedParameter> parameters, FileLoc loc)
        {
            if (upgrade)
            {
                if (definedTextures.ContainsKey(name))
                {
                    Warning(loc, StringPrintf("%s: renaming multiply-defined texture", name));
                    definedTextures[name] = StringPrintf("%s-renamed-%d", name, textureCount++);
                }
                else
                    definedTextures[name] = name;
            }

            if (upgrade && texname == "scale")
            {
                // This is easier to do in the raw ParsedParameterVector...
                if (type == "float")
                {
                    foreach (ParsedParameter p in parameters)
                    {
                        if (p.name == "tex1")
                            p.name = "tex";
                        if (p.name == "tex2")
                            p.name = "scale";
                    }
                }
                else
                {
                    // more subtle: rename one of them as float, but need one of them
                    // to be an RGB and spectrally constant...
                    bool foundRGB = false, foundTexture = false;
                    foreach (ParsedParameter p in parameters)
                    {
                        if (p.name != "tex1" && p.name != "tex2")
                            continue;

                        if (p.type == "rgb")
                        {
                            if (foundRGB)
                            {
                                ErrorExitDeferred(
                                    p.loc,
                                    StringPrintf("Two \"rgb\" textures found for \"scale\" " +
                                                 "texture \"%s\". Please manually edit the file to " +
                                                 "upgrade.",
                                                 name));
                                return;
                            }
                            if (p.floats.size() != 3)
                            {
                                ErrorExitDeferred(
                                    p.loc, StringPrintf("Didn't find 3 values for \"rgb\" \"%s\".", p.name));
                                return;
                            }
                            if (p.floats[0] != p.floats[1] || p.floats[1] != p.floats[2])
                            {
                                ErrorExitDeferred(p.loc,
                                                  StringPrintf("Non-constant \"rgb\" value found for " +
                                                               "\"scale\" texture parameter \"%s\". Please " +
                                                               "manually " +
                                                               "edit the file to upgrade.",
                                                               p.name));
                                return;
                            }

                            foundRGB = true;
                            p.type = "float";
                            p.name = "scale";
                            p.floats.resize(1);
                        }
                        else
                        {
                            if (foundTexture)
                            {
                                ErrorExitDeferred(
                                    p.loc,
                                    StringPrintf("Two textures found for \"scale\" " +
                                                 "texture \"%s\". Please manually edit the file to " +
                                                 "upgrade.",
                                                 name));
                                return;
                            }
                            p.name = "tex";
                            foundTexture = true;
                        }
                    }
                }
            }

            ParameterDictionary dict = new ParameterDictionary(parameters, RGBColorSpace.sRGB);
            if (upgrade)
                dict.RenameUsedTextures(definedTextures);

            string extra = "";
            if (upgrade)
            {
                if (texname == "imagemap")
                {
                    List<byte> tri = dict.GetBoolArray("trilinear");
                    if (tri.Count == 1)
                    {
                        dict.RemoveBool("trilinear");
                        extra += indent(1) + "\"string filter\" ";
                        extra += tri[0] != 0u ? "\"trilinear\"\n" : "\"bilinear\"\n";
                    }
                }

                if (texname == "imagemap" || texname == "ptex")
                {
                    Float gamma = dict.GetOneFloat("gamma", 0);
                    if (gamma != 0)
                    {
                        dict.RemoveFloat("gamma");
                        extra +=
                            indent(1) + StringPrintf("\"string encoding \" \"gamma %f\" ", gamma);
                    }
                    else
                    {
                        List<byte> gammaB = dict.GetBoolArray("gamma");
                        if (gammaB.Count == 1)
                        {
                            dict.RemoveBool("gamma");
                            extra += indent(1) + "\"string encoding\" ";
                            extra += gammaB[0] != 0u ? "\"sRGB\"\n" : "\"linear\"\n";
                        }
                    }
                }
            }

            if (upgrade)
            {
                if (type == "color")
                    Printf("%sTexture \"%s\" \"spectrum\" \"%s\"\n", indent(),
                           definedTextures[name], texname);
                else
                    Printf("%sTexture \"%s\" \"%s\" \"%s\"\n", indent(), definedTextures[name],
                           type, texname);
            }
            else
                Printf("%sTexture \"%s\" \"%s\" \"%s\"\n", indent(), name, type, texname);

            Console.Out.Write(extra + dict.ToParameterList(catIndentCount));
        }

        string upgradeMaterialIndex(string name, ParameterDictionary dict, FileLoc loc)
        {
            if (name != "glass" && name != "uber")
                return "";

            string tex = dict.GetTexture("index");
            if (tex.Length != 0)
            {
                if (dict.GetTexture("eta").Length != 0)
                {
                    ErrorExitDeferred(
                        loc, StringPrintf("Material \"%s\" has both \"index\" and \"eta\" parameters.", name));
                    return "";
                }

                dict.RemoveTexture("index");
                return indent(1) + StringPrintf("\"texture eta\" \"%s\"\n", tex);
            }
            else
            {
                List<Float> index = dict.GetFloatArray("index");
                if (index.Count == 0)
                    return "";
                if (index.Count != 1)
                {
                    ErrorExitDeferred(loc, "Multiple values provided for \"index\" parameter.");
                    return "";
                }
                if (dict.GetFloatArray("eta").Count != 0)
                {
                    ErrorExitDeferred(
                        loc, StringPrintf("Material \"%s\" has both \"index\" and \"eta\" parameters.", name));
                    return "";
                }

                Float value = index[0];
                dict.RemoveFloat("index");
                return indent(1) + StringPrintf("\"float eta\" [ %f ]\n", value);
            }
        }

        string upgradeMaterial(ref string name, ParameterDictionary dict, FileLoc loc)
        {
            string extra = upgradeMaterialIndex(name, dict, loc);

            dict.RenameParameter("bumpmap", "displacement");

            string nameCopy = name;
            Func<string, Float, bool> removeParamSilentIfConstant = (string paramName, Float value) =>
            {
                pstd.optional<RGB> rgb = dict.GetOneRGB(paramName);
                bool matches = (rgb && rgb.value().r == value && rgb.value().g == value && rgb.value().b == value);

                if (!matches &&
                    dict.GetSpectrumArray(paramName, SpectrumType.Unbounded, null).Count != 0)
                    Warning(loc,
                            StringPrintf("Parameter is being removed when converting " +
                                         "to \"%s\" material: %s",
                                         nameCopy, dict.ToParameterDefinition(paramName)));
                dict.RemoveSpectrum(paramName);
                dict.RemoveTexture(paramName);
                return matches;
            };

            if (name == "uber")
            {
                name = "coateddiffuse";
                if (removeParamSilentIfConstant("Ks", 0))
                {
                    name = "diffuse";
                    dict.RemoveFloat("eta");
                    dict.RemoveFloat("roughness");
                }
                removeParamSilentIfConstant("Kr", 0);
                removeParamSilentIfConstant("Kt", 0);
                dict.RenameParameter("Kd", "reflectance");

                if (dict.GetTexture("opacity").Length != 0)
                {
                    ErrorExitDeferred(loc, "Non-opaque \"opacity\" texture in \"uber\" " +
                                           "material not supported " +
                                           "in pbrt-v4. Please edit the file manually.");
                    return "";
                }

                if (dict.GetSpectrumArray("opacity", SpectrumType.Unbounded, null).Count == 0)
                    return "";

                pstd.optional<RGB> opacity = dict.GetOneRGB("opacity");
                if (opacity && opacity.value().r == 1 && opacity.value().g == 1 && opacity.value().b == 1)
                {
                    dict.RemoveSpectrum("opacity");
                    return "";
                }

                ErrorExitDeferred(loc, "A non-opaque \"opacity\" in the \"uber\" " +
                                       "material is not supported " +
                                       "in pbrt-v4. Please edit the file manually.");
            }
            else if (name == "mix")
            {
                // Convert the amount to a scalar
                pstd.optional<RGB> rgb = dict.GetOneRGB("amount");
                if (rgb)
                {
                    if (rgb.value().r == rgb.value().g && rgb.value().g == rgb.value().b)
                        extra += indent(1) + StringPrintf("\"float amount\" [ %f ]\n", rgb.value().r);
                    else
                    {
                        Float avg = (rgb.value().r + rgb.value().g + rgb.value().b) / 3;
                        Warning(loc, StringPrintf("Changing RGB \"amount\" (%f, %f, %f) to scalar average %f",
                                                  rgb.value().r, rgb.value().g, rgb.value().b, avg));
                        extra += indent(1) + StringPrintf("\"float amount\" [ %f ]\n", avg);
                    }
                }
                else if (dict.GetSpectrumArray("amount", SpectrumType.Unbounded, null).Count >
                         0)
                    ErrorExitDeferred(
                        loc, StringPrintf("Unable to update non-RGB spectrum \"amount\" to a scalar: %s",
                                          dict.ToParameterDefinition("amount")));

                dict.RemoveSpectrum("amount");

                // And rename...
                string m1 = dict.GetOneString("namedmaterial1", "");
                if (m1.Length == 0)
                    ErrorExitDeferred(
                        loc, "Didn't find \"namedmaterial1\" parameter for \"mix\" material.");
                dict.RemoveString("namedmaterial1");

                string m2 = dict.GetOneString("namedmaterial2", "");
                if (m2.Length == 0)
                    ErrorExitDeferred(
                        loc, "Didn't find \"namedmaterial2\" parameter for \"mix\" material.");
                dict.RemoveString("namedmaterial2");

                // Note: swapped order vs pbrt-v3!
                extra +=
                    indent(1) + StringPrintf("\"string materials\" [ \"%s\" \"%s\" ]\n", m2, m1);
            }
            else if (name == "substrate")
            {
                name = "coateddiffuse";
                removeParamSilentIfConstant("Ks", 1);
                dict.RenameParameter("Kd", "reflectance");
            }
            else if (name == "glass")
            {
                name = "dielectric";
                removeParamSilentIfConstant("Kr", 1);
                removeParamSilentIfConstant("Kt", 1);
            }
            else if (name == "plastic")
            {
                name = "coateddiffuse";
                if (removeParamSilentIfConstant("Ks", 0))
                {
                    name = "diffuse";
                    dict.RemoveFloat("roughness");
                    dict.RemoveFloat("eta");
                }
                dict.RenameParameter("Kd", "reflectance");
            }
            else if (name == "fourier")
                Warning(loc, "\"fourier\" material is no longer supported. (But there " +
                             "is \"measured\"!)");
            else if (name == "kdsubsurface")
            {
                name = "subsurface";
                dict.RenameParameter("Kd", "reflectance");
            }
            else if (name == "matte")
            {
                name = "diffuse";
                dict.RenameParameter("Kd", "reflectance");
                dict.RemoveFloat("sigma");
                dict.RemoveTexture("sigma");
            }
            else if (name == "metal")
            {
                name = "conductor";
                removeParamSilentIfConstant("Kr", 1);
            }
            else if (name == "translucent")
            {
                name = "diffusetransmission";

                dict.RenameParameter("Kd", "transmittance");

                removeParamSilentIfConstant("reflect", 0);
                removeParamSilentIfConstant("transmit", 1);

                removeParamSilentIfConstant("Ks", 0);
                dict.RemoveFloat("roughness");
            }
            else if (name == "mirror")
            {
                name = "conductor";
                extra += indent(1) + "\"float roughness\" [ 0 ]\n";
                extra += indent(1) + "\"spectrum eta\" [ \"metal-Ag-eta\" ]\n";
                extra += indent(1) + "\"spectrum k\" [ \"metal-Ag-k\" ]\n";

                removeParamSilentIfConstant("Kr", 0);
            }
            else if (name == "disney")
            {
                name = "diffuse";
                dict.RenameParameter("color", "reflectance");
            }
            else if (name == "hair")
            {
                dict.RenameParameter("color", "reflectance");
            }
            else if (name.Length == 0 || name == "none")
                name = "interface";

            return extra;
        }

        public override void Material(string name, List<ParsedParameter> parameters, FileLoc loc)
        {
            ParameterDictionary dict = new ParameterDictionary(parameters, RGBColorSpace.sRGB);
            if (upgrade)
                dict.RenameUsedTextures(definedTextures);

            string extra = "";
            string newName = name;
            if (upgrade)
                extra = upgradeMaterial(ref newName, dict, loc);

            Printf("%sMaterial \"%s\"\n", indent(), newName);
            Console.Out.Write(extra + dict.ToParameterList(catIndentCount));
        }

        static int namedMaterialCount = 0;

        public override void MakeNamedMaterial(string name, List<ParsedParameter> parameters, FileLoc loc)
        {
            ParameterDictionary dict = new ParameterDictionary(parameters, RGBColorSpace.sRGB);

            if (upgrade)
            {
                dict.RenameUsedTextures(definedTextures);

                if (definedNamedMaterials.ContainsKey(name))
                {
                    Warning(loc, StringPrintf("%s: renaming multiply-defined named material", name));
                    definedNamedMaterials[name] = StringPrintf("%s-renamed-%d", name, namedMaterialCount++);
                }
                else
                    definedNamedMaterials[name] = name;
                Printf("%sMakeNamedMaterial \"%s\"\n", indent(), definedNamedMaterials[name]);
            }
            else
                Printf("%sMakeNamedMaterial \"%s\"\n", indent(), name);

            string extra = "";
            if (upgrade)
            {
                string matName = dict.GetOneString("type", "");
                extra = upgradeMaterial(ref matName, dict, loc);
                dict.RemoveString("type");
                extra = indent(1) + StringPrintf("\"string type\" [ \"%s\" ]\n", matName) + extra;
            }
            Console.Out.Write(extra + dict.ToParameterList(catIndentCount));

            if (upgrade)
                namedMaterialDictionaries[definedNamedMaterials[name]] = dict;
        }

        public override void NamedMaterial(string name, FileLoc loc)
        {
            Printf("%sNamedMaterial \"%s\"\n", indent(), name);
        }

        static bool upgradeRGBToScale(ParameterDictionary dict, string name,
                                      ref Float totalScale)
        {
            List<Spectrum> s = dict.GetSpectrumArray(name, SpectrumType.Unbounded, null);
            if (s.Count == 0)
                return true;

            pstd.optional<RGB> rgb = dict.GetOneRGB(name);
            if (!rgb || rgb.value().r != rgb.value().g || rgb.value().g != rgb.value().b)
                return false;

            totalScale *= rgb.value().r;
            dict.RemoveSpectrum(name);
            return true;
        }

        static string upgradeMapname(FormattingParserTarget scene, ParameterDictionary dict)
        {
            string n = dict.GetOneString("mapname", "");
            if (n.Length == 0)
                return "";

            dict.RemoveString("mapname");
            return scene.indent(1) + StringPrintf("\"string filename\" \"%s\"\n", n);
        }

        public override void LightSource(string name, List<ParsedParameter> parameters, FileLoc loc)
        {
            ParameterDictionary dict = new ParameterDictionary(parameters, RGBColorSpace.sRGB);

            Printf("%sLightSource \"%s\"\n", indent(), name);

            string extra = "";
            if (upgrade)
            {
                Float totalScale = 1;
                if (!upgradeRGBToScale(dict, "scale", ref totalScale))
                {
                    ErrorExitDeferred(dict.loc("scale"),
                                      "In pbrt-v4, \"scale\" is a \"float\" parameter " +
                                      "to light sources. " +
                                      "Please modify your scene file manually.");
                    return;
                }
                dict.RemoveInt("samples");

                if (dict.GetOneString("mapname", "").Length != 0)
                {
                    if (name == "infinite" && !upgradeRGBToScale(dict, "L", ref totalScale))
                    {
                        ErrorExitDeferred(
                            dict.loc("L"),
                            "Non-constant \"L\" is no longer supported with " +
                            "\"mapname\" for " +
                            "the \"infinite\" light source. Please upgrade your scene " +
                            "file manually.");
                        return;
                    }
                    else if (name == "projection" &&
                             !upgradeRGBToScale(dict, "I", ref totalScale))
                    {
                        ErrorExitDeferred(
                            dict.loc("I"),
                            "\"I\" is no longer supported with \"mapname\" for " +
                            "the \"projection\" light source. Please upgrade your scene " +
                            "file manually.");
                        return;
                    }
                }

                totalScale *= dict.UpgradeBlackbody("I");
                totalScale *= dict.UpgradeBlackbody("L");

                // Do this after we've handled infinite "L" with a map, since
                // it removes the "mapname" parameter from the dictionary.
                extra += upgradeMapname(this, dict);

                if (totalScale != 1)
                {
                    totalScale *= dict.GetOneFloat("scale", 1.0f);
                    dict.RemoveFloat("scale");
                    Printf("%s\"float scale\" [%f]\n", indent(1), totalScale);
                }
            }

            Console.Out.Write(extra + dict.ToParameterList(catIndentCount));
        }

        public override void AreaLightSource(string name, List<ParsedParameter> parameters, FileLoc loc)
        {
            ParameterDictionary dict = new ParameterDictionary(parameters, RGBColorSpace.sRGB);
            string extra = "";
            Float totalScale = 1;
            if (upgrade)
            {
                if (!upgradeRGBToScale(dict, "scale", ref totalScale))
                {
                    ErrorExitDeferred(dict.loc("scale"),
                                      "In pbrt-v4, \"scale\" is a \"float\" parameter " +
                                      "to light sources. " +
                                      "Please modify your scene file manually.");
                    return;
                }

                totalScale *= dict.UpgradeBlackbody("L");

                if (name == "area")
                    Printf("%sAreaLightSource \"diffuse\"\n", indent());
                else
                    Printf("%sAreaLightSource \"%s\"\n", indent(), name);
                dict.RemoveInt("nsamples");
            }
            else
                Printf("%sAreaLightSource \"%s\"\n", indent(), name);

            if (totalScale != 1)
                Printf("%s\"float scale\" [%f]\n", indent(1), totalScale);
            Console.Out.Write(extra + dict.ToParameterList(catIndentCount));
        }

        static string upgradeTriMeshUVs(FormattingParserTarget scene, ParameterDictionary dict)
        {
            List<Point2f> uv = dict.GetPoint2fArray("st");
            if (uv.Count != 0)
                dict.RemovePoint2f("st");
            else
            {
                Action<string> upgradeFloatArray = (string name) =>
                {
                    List<Float> fuv = dict.GetFloatArray(name);
                    if (fuv.Count == 0)
                        return;

                    List<Point2f> tempUVs = new List<Point2f>(fuv.Count / 2);
                    for (int i = 0; i < fuv.Count / 2; ++i)
                        tempUVs.Add(new Point2f(fuv[2 * i], fuv[2 * i + 1]));
                    dict.RemoveFloat(name);
                    uv = tempUVs;
                };
                upgradeFloatArray("uv");
                upgradeFloatArray("st");
            }

            if (uv.Count == 0)
                return "";

            string s = scene.indent(1) + "\"point2 uv\" [ ";
            for (int i = 0; i < uv.Count; ++i)
            {
                s += StringPrintf("%f %f ", uv[i][0], uv[i][1]);
                if ((i + 1) % 4 == 0)
                {
                    s += "\n";
                    s += scene.indent(2);
                }
            }
            s += "]\n";
            return s;
        }

        static int plyCount = 1;

        public override void Shape(string name, List<ParsedParameter> parameters, FileLoc loc)
        {
            ParameterDictionary dict = new ParameterDictionary(parameters, RGBColorSpace.sRGB);

            if (toPly && name == "trianglemesh")
            {
                List<int> vi = dict.GetIntArray("indices");

                if (vi.Count < 500)
                {
                    // It's a small mesh; don't bother with a PLY file after all.
                    Printf("%sShape \"%s\"\n", indent(), name);
                    Console.Out.Write(dict.ToParameterList(catIndentCount));
                }
                else
                {
                    string plyPrefix = Environment.GetEnvironmentVariable("PLY_PREFIX") ?? "mesh";
                    string fn = StringPrintf("%s_%05d.ply", plyPrefix, plyCount++);

                    global::pbrt.Transform identity = new global::pbrt.Transform();
                    TriangleMesh mesh =
                        global::pbrt.Triangle.CreateMesh(identity, false, dict, loc, new Allocator());
                    if (!mesh.WritePLY(fn))
                        ErrorExit(loc, StringPrintf("%s: unable to write PLY file.", fn));

                    dict.RemoveInt("indices");
                    dict.RemovePoint3f("P");
                    dict.RemovePoint2f("uv");
                    dict.RemoveNormal3f("N");
                    dict.RemoveVector3f("S");
                    dict.RemoveInt("faceIndices");

                    Printf("%sShape \"plymesh\" \"string filename\" \"%s\"\n", indent(), fn);
                    Console.Out.Write(dict.ToParameterList(catIndentCount));
                }
                return;
            }

            Printf("%sShape \"%s\"\n", indent(), name);

            if (upgrade)
            {
                if (name == "trianglemesh")
                {
                    // Remove indices if they're [0 1 2] and we have a single triangle
                    List<int> indices = dict.GetIntArray("indices");
                    if (indices.Count == 3 && dict.GetPoint3fArray("P").Count == 3 &&
                        indices[0] == 0 && indices[1] == 1 && indices[2] == 2)
                        dict.RemoveInt("indices");
                }

                if (name == "bilinearmesh")
                {
                    // Remove indices if they're [0 1 2 3] and we have a single blp
                    List<int> indices = dict.GetIntArray("indices");
                    if (indices.Count == 4 && dict.GetPoint3fArray("P").Count == 4 &&
                        indices[0] == 0 && indices[1] == 1 && indices[2] == 2 && indices[3] == 3)
                        dict.RemoveInt("indices");
                }

                if (name == "loopsubdiv")
                {
                    List<int> levels = dict.GetIntArray("nlevels");
                    if (levels.Count != 0)
                    {
                        Printf("%s\"integer levels\" [ %d ]\n", indent(1), levels[0]);
                        dict.RemoveInt("nlevels");
                    }
                }
                if (name == "trianglemesh" || name == "plymesh")
                {
                    dict.RemoveBool("discarddegenerateUVs");
                    dict.RemoveTexture("shadowalpha");
                }

                if (name == "trianglemesh")
                {
                    string extra = upgradeTriMeshUVs(this, dict);
                    Console.Out.Write(extra);
                }

                dict.RenameParameter("Kd", "reflectance");
            }

            Console.Out.Write(dict.ToParameterList(catIndentCount));
        }

        public override void ReverseOrientation(FileLoc loc)
        {
            Printf("%sReverseOrientation\n", indent());
        }

        static int objectCount = 0;

        public override void ObjectBegin(string name, FileLoc loc)
        {
            if (upgrade)
            {
                if (definedObjectInstances.ContainsKey(name))
                {
                    Warning(loc, StringPrintf("%s: renaming multiply-defined object instance", name));
                    definedObjectInstances[name] = StringPrintf("%s-renamed-%d", name, objectCount++);
                }
                else
                    definedObjectInstances[name] = name;
                Printf("%sObjectBegin \"%s\"\n", indent(), definedObjectInstances[name]);
            }
            else
                Printf("%sObjectBegin \"%s\"\n", indent(), name);
        }

        public override void ObjectEnd(FileLoc loc)
        {
            Printf("%sObjectEnd\n", indent());
        }

        public override void ObjectInstance(string name, FileLoc loc)
        {
            if (upgrade)
            {
                if (!definedObjectInstances.ContainsKey(name))
                    // this is legit if we're upgrading multiple files separately...
                    Printf("%sObjectInstance \"%s\"\n", indent(), name);
                else
                    // use the most recent renaming of it
                    Printf("%sObjectInstance \"%s\"\n", indent(), definedObjectInstances[name]);
            }
            else
                Printf("%sObjectInstance \"%s\"\n", indent(), name);
        }

        public override void EndOfFiles() { }

        int catIndentCount = 0;
        bool toPly, upgrade;
        SortedDictionary<string, string> definedTextures = new SortedDictionary<string, string>(StringComparer.Ordinal);
        SortedDictionary<string, string> definedNamedMaterials = new SortedDictionary<string, string>(StringComparer.Ordinal);
        SortedDictionary<string, ParameterDictionary> namedMaterialDictionaries = new SortedDictionary<string, ParameterDictionary>(StringComparer.Ordinal);
        SortedDictionary<string, string> definedObjectInstances = new SortedDictionary<string, string>(StringComparer.Ordinal);
    }

}  // namespace pbrt
