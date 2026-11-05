// OSL port: liboslexec/osoreader.h, osolex.l, osogram.y
//
// The reader of .oso files: the base class OSOReader whose virtual callbacks are what a consumer overrides, the scanner of osolex.l
// written out as code (flex's rules: the longest match, the earliest rule among equals) and the parser of osogram.y, whose LALR(1)
// tables are the ones bison generates (osogram_tables.cs) driven by bison's yyparse loop.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using OIIO;

namespace OSL
{
    namespace pvt
    {
        /// Base class for OSO (OpenShadingLanguage object code) file reader.
        public class OSOReader
        {
            private ErrorHandler m_err;
            private int m_lineno;
            private TypeSpec m_current_typespec = new TypeSpec();

            public OSOReader(ErrorHandler errhandler = null)
            {
                m_err = errhandler != null ? errhandler : ErrorHandler.default_handler;
                m_lineno = 1;
            }

            /// Read in the oso file, parse it, call the various callbacks. Return true if the file was correctly parsed, false if there was an
            /// unrecoverable error reading the file.
            public virtual bool parse_file(string filename)
            {
                // The lexer/parser isn't thread-safe in the C++, so only one thread can actually be reading a .oso file at a time
                string text;
                try
                {
                    text = File.ReadAllText(filename);
                }
                catch (Exception)
                {
                    m_err.errorfmt("File " + filename + " not found");
                    return false;
                }
                return OsoParser.parse(this, text, filename);
            }

            /// Read in OSO from memory, parse, call the various callbacks. Return true if the OSO code was correctly parsed, false if there was
            /// an unrecoverable error reading.
            public virtual bool parse_memory(string buffer)
            {
                return OsoParser.parse(this, buffer, "preloaded OSO code");
            }

            /// Declare the shader version.
            public virtual void version(string specid, int major, int minor) { }

            /// Set the name and type of the shader
            public virtual void shader(string shadertype, string name) { }

            /// Register a new symbol.
            public virtual void symbol(SymType symtype, TypeSpec typespec, string name) { }

            /// Add a default integer value to the last declared symbol.
            public virtual void symdefault(int def) { }

            /// Add a default float value to the last declared symbol.
            public virtual void symdefault(float def) { }

            /// Add a default string value to the last declared symbol.
            public virtual void symdefault(string def) { }

            /// Called when we're done with all information related to a parameter symbol.
            public virtual void parameter_done() { }

            /// Return true for parsers whose only purpose is to read the header up to params, to stop parsing as soon as we start encountering
            /// temps in the symbol table.
            public virtual bool stop_parsing_at_temp_symbols() { return false; }

            /// Add a hint.
            public virtual void hint(string hintstring) { }

            /// Return true if this parser cares about the code, false if parsing of oso may terminate once the symbol table has been parsed.
            public virtual bool parse_code_section() { return true; }

            /// New code section marker designating subsequent instructions.
            public virtual void codemarker(string name) { }

            /// Mark the end of the code section
            public virtual void codeend() { }

            /// Add an instruction.
            public virtual void instruction(int label, string opcode) { }

            /// Add an argument to the last instruction.
            public virtual void instruction_arg(string name) { }

            /// Add a jump target to the last instruction.
            public virtual void instruction_jump(int target) { }

            /// Called after an instruction (after args and hints)
            public virtual void instruction_end() { }

            /// Increment the line number (for error reporting). Should only be called by the lexer.
            public void incr_lineno() { ++m_lineno; }

            /// Return the line number (for error reporting). Should only be called by the lexer.
            public int lineno() { return m_lineno; }

            /// Return a reference to the error handler
            public ErrorHandler errhandler() { return m_err; }

            /// Store and retrieve the current typespec
            public void current_typespec(TypeSpec t) { m_current_typespec = t; }
            public TypeSpec current_typespec() { return m_current_typespec; }
        }

        internal struct OsoYYSTYPE
        {
            public int i;
            public float f;
            public string s;
        }

        internal static class OsoTokens
        {
            public const int IDENTIFIER = 258, STRING_LITERAL = 259, HINT = 260, INT_LITERAL = 261, FLOAT_LITERAL = 262, COLORTYPE = 263,
                             FLOATTYPE = 264, INTTYPE = 265, MATRIXTYPE = 266, NORMALTYPE = 267, POINTTYPE = 268, STRINGTYPE = 269,
                             VECTORTYPE = 270, VOIDTYPE = 271, CLOSURE = 272, STRUCT = 273, CODE = 274, SYMTYPE = 275, ENDOFLINE = 276;
        }

        /// The scanner of osolex.l
        internal sealed class OsoLexer
        {
            private const string E = @"[eE][-+]?[0-9]+";
            private const string FLT = @"(?:[-+]?[0-9]+\.[0-9]*(?:" + E + @")?|[-+]?[0-9]*\.[0-9]+(?:" + E + @")?|[-+]?[0-9]+" + E + ")";
            private const string STR = "\"(?:\\\\.|[^\\\\\"\\n])*\"";
            private const string INTEGER = @"[-+]?[0-9]+";
            private const string IDENT = @"(?:[A-Za-z_$][A-Za-z0-9_$.]*)";
            private const string IDORLITERAL = "(?:" + FLT + "|" + STR + "|" + INTEGER + "|" + IDENT + @"(?:\[(?:" + INTEGER + @")?\])?)";
            private const string HINTPATTERN = "%" + IDENT + @"(?:\{(?:" + IDORLITERAL + "(?:," + IDORLITERAL + @")*)?\})?";

            private static readonly RegexOptions options = RegexOptions.Compiled | RegexOptions.CultureInvariant;
            private static readonly Regex reFlt = new Regex(@"\G" + FLT, options);
            private static readonly Regex reInt = new Regex(@"\G" + INTEGER, options);
            private static readonly Regex reStr = new Regex(@"\G" + STR, options);
            private static readonly Regex reHint = new Regex(@"\G" + HINTPATTERN, options);
            private static readonly Regex reIdent = new Regex(@"\G" + IDENT, options);
            private static readonly Regex reComment = new Regex(@"\G#[^\n]*\n", options);
            private static readonly Regex rePreprocessed = new Regex(@"\G%preprocessed_source\n(?:[^\n]*\n)*", options);

            private string text;
            private int pos;
            private bool declaration;  // the DECLARATION start condition
            private OSOReader reader;

            public OsoLexer(string text, OSOReader reader)
            {
                this.text = text;
                this.reader = reader;
                pos = 0;
            }

            public int osolex(out OsoYYSTYPE yylval)
            {
                yylval = new OsoYYSTYPE();
                while (pos < text.Length)
                {
                    char c = text[pos];
                    bool bol = pos == 0 || text[pos - 1] == '\n';

                    if (c == '#')
                    {
                        Match m = reComment.Match(text, pos);
                        if (m.Success)
                        {
                            pos += m.Length;  // skip it
                            continue;
                        }
                    }

                    if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '_' || c == '$')
                    {
                        Match m = reIdent.Match(text, pos);
                        string word = m.Value;
                        pos += m.Length;
                        if (declaration)
                        {
                            switch (word)
                            {
                                case "closure": return yylval.i = OsoTokens.CLOSURE;
                                case "color": return yylval.i = OsoTokens.COLORTYPE;
                                case "float": return yylval.i = OsoTokens.FLOATTYPE;
                                case "int": return yylval.i = OsoTokens.INTTYPE;
                                case "matrix": return yylval.i = OsoTokens.MATRIXTYPE;
                                case "normal": return yylval.i = OsoTokens.NORMALTYPE;
                                case "point": return yylval.i = OsoTokens.POINTTYPE;
                                case "string": return yylval.i = OsoTokens.STRINGTYPE;
                                case "struct": return yylval.i = OsoTokens.STRUCT;
                                case "vector": return yylval.i = OsoTokens.VECTORTYPE;
                            }
                        }
                        if (bol)
                        {
                            switch (word)
                            {
                                case "local":
                                    declaration = true;
                                    yylval.i = (int)SymType.SymTypeLocal;
                                    return OsoTokens.SYMTYPE;
                                case "temp":
                                    declaration = true;
                                    yylval.i = (int)SymType.SymTypeTemp;
                                    return OsoTokens.SYMTYPE;
                                case "global":
                                    declaration = true;
                                    yylval.i = (int)SymType.SymTypeGlobal;
                                    return OsoTokens.SYMTYPE;
                                case "param":
                                    declaration = true;
                                    yylval.i = (int)SymType.SymTypeParam;
                                    return OsoTokens.SYMTYPE;
                                case "oparam":
                                    declaration = true;
                                    yylval.i = (int)SymType.SymTypeOutputParam;
                                    return OsoTokens.SYMTYPE;
                                case "const":
                                    declaration = true;
                                    yylval.i = (int)SymType.SymTypeConst;
                                    return OsoTokens.SYMTYPE;
                                case "code":
                                    declaration = false;
                                    return yylval.i = OsoTokens.CODE;
                            }
                        }
                        yylval.s = string.Intern(word);
                        return OsoTokens.IDENTIFIER;
                    }

                    if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.')
                    {
                        Match mf = reFlt.Match(text, pos);
                        Match mi = reInt.Match(text, pos);
                        int lf = mf.Success ? mf.Length : 0;
                        int li = mi.Success ? mi.Length : 0;
                        if (li >= lf && li > 0)
                        {
                            yylval.i = int.Parse(mi.Value, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture);
                            pos += li;
                            return OsoTokens.INT_LITERAL;
                        }
                        if (lf > 0)
                        {
                            yylval.f = float.Parse(mf.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
                            pos += lf;
                            return OsoTokens.FLOAT_LITERAL;
                        }
                    }

                    if (c == '"')
                    {
                        Match m = reStr.Match(text, pos);
                        if (m.Success)
                        {
                            // grab the material between the quotes
                            string s = m.Value.Substring(1, m.Length - 2);
                            if (s.IndexOf('\\') >= 0)
                            {
                                // Only make a new string if we must unescape
                                s = Strutil.unescape_chars(s);
                            }
                            yylval.s = string.Intern(s);
                            pos += m.Length;
                            return OsoTokens.STRING_LITERAL;
                        }
                    }

                    if (c == '%')
                    {
                        Match mp = rePreprocessed.Match(text, pos);
                        Match mh = reHint.Match(text, pos);
                        if (mp.Success && mp.Length >= (mh.Success ? mh.Length : 0))
                        {
                            pos += mp.Length;  // skip remainder of file
                            continue;
                        }
                        if (mh.Success)
                        {
                            yylval.s = string.Intern(mh.Value);
                            pos += mh.Length;
                            return OsoTokens.HINT;
                        }
                    }

                    // Ignore whitespace
                    if (c == ' ' || c == '\t' || c == '\v' || c == '\f' || c == '\r')
                    {
                        ++pos;
                        continue;
                    }

                    // Ignore a linefeed that ends with a trailing blackslash
                    if (c == '\\' && pos + 1 < text.Length && text[pos + 1] == '\n')
                    {
                        pos += 2;
                        continue;
                    }

                    // End of line
                    if (c == '\n')
                    {
                        ++pos;
                        reader.incr_lineno();
                        return OsoTokens.ENDOFLINE;
                    }

                    // catch-all rule for any other single characters
                    ++pos;
                    return yylval.i = c;
                }
                return 0;
            }
        }

        /// The parser of osogram.y
        internal static class OsoParser
        {
            private static readonly object osoread_mutex = new object();

            // Convert from the lexer's symbolic type (COLORTYPE, etc.) to a TypeDesc.
            private static TypeDesc osolextype(int lex)
            {
                switch (lex)
                {
                    case OsoTokens.COLORTYPE: return TypeDescs.TypeColor;
                    case OsoTokens.FLOATTYPE: return TypeDescs.TypeFloat;
                    case OsoTokens.INTTYPE: return TypeDescs.TypeInt;
                    case OsoTokens.MATRIXTYPE: return TypeDescs.TypeMatrix;
                    case OsoTokens.NORMALTYPE: return TypeDescs.TypeNormal;
                    case OsoTokens.POINTTYPE: return TypeDescs.TypePoint;
                    case OsoTokens.STRINGTYPE: return TypeDescs.TypeString;
                    case OsoTokens.VECTORTYPE: return TypeDescs.TypeVector;
                    case OsoTokens.VOIDTYPE: return new TypeDesc(TypeDesc.NONE);
                    default: return new TypeDesc(TypeDesc.UNKNOWN);
                }
            }

            public static bool parse(OSOReader reader, string text, string what)
            {
                lock (osoread_mutex)
                {
                    OsoLexer scanner = new OsoLexer(text, reader);
                    int errcode = osoparse(scanner, reader);  // osoparse returns nonzero if error
                    if (errcode != 0)
                    {
                        reader.errhandler().errorfmt("Failed parse of " + what + " (error code " + errcode + ")");
                    }
                    return errcode == 0;
                }
            }

            private const int YYEMPTY = -2;

            /// bison's yyparse for osogram.y
            private static int osoparse(OsoLexer scanner, OSOReader osoreader)
            {
                short[] yytranslate = OsoGramTables.yytranslate, yyr1 = OsoGramTables.yyr1, yyr2 = OsoGramTables.yyr2;
                short[] yypact = OsoGramTables.yypact, yydefact = OsoGramTables.yydefact, yypgoto = OsoGramTables.yypgoto;
                short[] yydefgoto = OsoGramTables.yydefgoto, yytable = OsoGramTables.yytable, yycheck = OsoGramTables.yycheck;

                List<int> yyss = new List<int>();
                List<OsoYYSTYPE> yyvs = new List<OsoYYSTYPE>();

                int yystate = 0;
                int yychar = YYEMPTY;
                OsoYYSTYPE yylval = new OsoYYSTYPE();

                yyss.Add(0);
                yyvs.Add(new OsoYYSTYPE());

                int yyn;
                int yytoken = 0;
                while (true)
                {
                    if (yystate == OsoGramTables.YYFINAL)
                    {
                        return 0;  // YYACCEPT
                    }

                    bool reduce = false;
                    yyn = yypact[yystate];
                    if (yyn != OsoGramTables.YYPACT_NINF)
                    {
                        if (yychar == YYEMPTY)
                        {
                            yychar = scanner.osolex(out yylval);
                        }
                        if (yychar <= 0)
                        {
                            yychar = 0;
                            yytoken = 0;
                        }
                        else
                        {
                            yytoken = (0 <= yychar && yychar <= OsoGramTables.YYMAXUTOK) ? yytranslate[yychar] : 2;
                        }

                        yyn += yytoken;
                        if (yyn < 0 || OsoGramTables.YYLAST < yyn || yycheck[yyn] != yytoken)
                        {
                            yyn = yydefact[yystate];
                            if (yyn == 0)
                            {
                                yyerror(osoreader, "syntax error");
                                return 1;
                            }
                            reduce = true;
                        }
                        else
                        {
                            yyn = yytable[yyn];
                            if (yyn <= 0)
                            {
                                if (yyn == OsoGramTables.YYTABLE_NINF)
                                {
                                    yyerror(osoreader, "syntax error");
                                    return 1;
                                }
                                yyn = -yyn;
                                reduce = true;
                            }
                            else
                            {
                                yystate = yyn;
                                yyss.Add(yystate);
                                yyvs.Add(yylval);
                                yychar = YYEMPTY;
                                continue;
                            }
                        }
                    }
                    else
                    {
                        yyn = yydefact[yystate];
                        if (yyn == 0)
                        {
                            yyerror(osoreader, "syntax error");
                            return 1;
                        }
                        reduce = true;
                    }

                    if (reduce)
                    {
                        int yylen = yyr2[yyn];
                        int top = yyvs.Count - 1;
                        OsoYYSTYPE yyval = yylen > 0 ? yyvs[top + 1 - yylen] : new OsoYYSTYPE();

                        if (reduce_action(osoreader, yyn, yyvs, top, ref yyval))
                        {
                            return 0;  // YYACCEPT from an action
                        }

                        yyss.RemoveRange(yyss.Count - yylen, yylen);
                        yyvs.RemoveRange(yyvs.Count - yylen, yylen);
                        yyvs.Add(yyval);

                        yyn = yyr1[yyn];
                        int top_state = yyss[yyss.Count - 1];
                        yystate = yypgoto[yyn - OsoGramTables.YYNTOKENS] + top_state;
                        if (0 <= yystate && yystate <= OsoGramTables.YYLAST && yycheck[yystate] == top_state)
                        {
                            yystate = yytable[yystate];
                        }
                        else
                        {
                            yystate = yydefgoto[yyn - OsoGramTables.YYNTOKENS];
                        }
                        yyss.Add(yystate);
                    }
                }
            }

            private static void yyerror(OSOReader osoreader, string err)
            {
                osoreader.errhandler().errorfmt("Error, line : ");
            }

            // Returns true for YYACCEPT. $k of a rule counts from the symbol that begins its right-hand side; a mid-rule action sees only the
            // symbols that precede it.
            private static bool reduce_action(OSOReader osoreader, int yyn, List<OsoYYSTYPE> vs, int top, ref OsoYYSTYPE yyval)
            {
                int len = OsoGramTables.yyr2[yyn];
                switch (yyn)
                {
                    // oso_file: version shader_declaration symbols_opt codemarker instructions
                    case 2:
                        osoreader.codeend();
                        yyval.i = 0;
                        break;

                    // version: IDENTIFIER FLOAT_LITERAL ENDOFLINE
                    case 3:
                        {
                            float f = vs[top + 2 - len].f;
                            int major = (int)f;
                            int minor = (int)(100 * (f - major) + 0.5);
                            osoreader.version(vs[top + 1 - len].s, major, minor);
                            yyval.i = 0;
                        }
                        break;

                    // $@1: after "shader_type IDENTIFIER"
                    case 4:
                        len = 2;
                        osoreader.shader(vs[top + 1 - len].s, vs[top + 2 - len].s);
                        break;

                    // shader_declaration: shader_type IDENTIFIER $@1 hints_opt ENDOFLINE
                    case 5:
                        yyval.i = 0;
                        break;

                    // symbols_opt: symbols | %empty
                    case 6:
                    case 7:
                        yyval.i = 0;
                        break;

                    // codemarker: CODE IDENTIFIER ENDOFLINE
                    case 8:
                        if (!osoreader.parse_code_section())
                        {
                            return true;  // YYACCEPT
                        }
                        osoreader.codemarker(vs[top + 2 - len].s);
                        break;

                    // $@2: after "label opcode"
                    case 11:
                        len = 2;
                        osoreader.instruction(vs[top + 1 - len].i, vs[top + 2 - len].s);
                        break;

                    // instruction: label opcode $@2 arguments_opt jumptargets_opt hints_opt ENDOFLINE
                    case 12:
                        osoreader.instruction_end();
                        break;

                    // $@3: after "SYMTYPE typespec arraylen_opt IDENTIFIER"
                    case 18:
                        {
                            len = 4;
                            if ((SymType)vs[top + 1 - len].i == SymType.SymTypeTemp && osoreader.stop_parsing_at_temp_symbols())
                            {
                                return true;  // YYACCEPT
                            }
                            TypeSpec typespec = osoreader.current_typespec();
                            if (vs[top + 3 - len].i != 0)
                            {
                                typespec.make_array(vs[top + 3 - len].i);
                            }
                            osoreader.symbol((SymType)vs[top + 1 - len].i, typespec, vs[top + 4 - len].s);
                        }
                        break;

                    // $@4: after "... initial_values_opt hints_opt"
                    case 19:
                        osoreader.parameter_done();
                        break;

                    // typespec: simple_typename
                    case 22:
                        osoreader.current_typespec(new TypeSpec(osolextype(vs[top + 1 - len].i)));
                        yyval.i = 0;
                        break;

                    // typespec: CLOSURE simple_typename
                    case 23:
                        osoreader.current_typespec(new TypeSpec(osolextype(vs[top + 2 - len].i), true));
                        yyval.i = 0;
                        break;

                    // typespec: STRUCT IDENTIFIER
                    case 24:
                        osoreader.current_typespec(new TypeSpec(vs[top + 2 - len].s, 0));
                        yyval.i = 0;
                        break;

                    // arraylen_opt: '[' INT_LITERAL ']'
                    case 34:
                        yyval.i = vs[top + 2 - len].i;
                        break;

                    // arraylen_opt: '[' ']'
                    case 35:
                        yyval.i = -1;
                        break;

                    // arraylen_opt: %empty
                    case 36:
                        yyval.i = 0;
                        break;

                    // initial_values_opt: %empty
                    case 38:
                        yyval.i = 0;
                        break;

                    // initial_value: FLOAT_LITERAL
                    case 41:
                        osoreader.symdefault(vs[top + 1 - len].f);
                        yyval.i = 0;
                        break;

                    // initial_value: INT_LITERAL
                    case 42:
                        osoreader.symdefault(vs[top + 1 - len].i);
                        yyval.i = 0;
                        break;

                    // initial_value: STRING_LITERAL
                    case 43:
                        osoreader.symdefault(vs[top + 1 - len].s);
                        yyval.i = 0;
                        break;

                    // label: %empty
                    case 45:
                        yyval.i = -1;
                        break;

                    // arguments_opt: %empty
                    case 48:
                        yyval.i = 0;
                        break;

                    // argument: IDENTIFIER
                    case 51:
                        osoreader.instruction_arg(vs[top + 1 - len].s);
                        break;

                    // jumptargets_opt: %empty
                    case 53:
                        yyval.i = 0;
                        break;

                    // jumptarget: INT_LITERAL
                    case 56:
                        osoreader.instruction_jump(vs[top + 1 - len].i);
                        break;

                    // hints_opt: %empty
                    case 58:
                        yyval.i = 0;
                        break;

                    // hint: HINT
                    case 61:
                        osoreader.hint(vs[top + 1 - len].s);
                        yyval.i = 0;
                        break;

                    default:
                        // The rest are the default action $$ = $1 (already in yyval)
                        break;
                }
                return false;
            }
        }
    }
}
