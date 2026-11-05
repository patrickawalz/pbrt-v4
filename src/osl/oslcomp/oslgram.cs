// OSL port: liboslcomp/oslgram.y
//
// The parser. The LALR(1) tables are the ones bison generates from oslgram.y (oslgram_tables.cs); this file is bison's yacc.c driver
// (yyparse) written out in C#, with the semantic actions of the grammar in the reduce switch below. Rule numbers are bison's own (the
// number in the .output listing plus one, as in the generated parser) and the grammar rule is quoted at each case.

using System;
using System.Collections.Generic;
using OIIO;

namespace OSL
{
    namespace pvt
    {
        public partial class OSLCompilerImpl
        {
            // Convert from the lexer's symbolic type (COLORTYPE, etc.) to a TypeDesc.
            internal static TypeDesc osllextype(int lex)
            {
                switch (lex)
                {
                    case OslTokens.COLORTYPE: return TypeDescs.TypeColor;
                    case OslTokens.FLOATTYPE: return TypeDescs.TypeFloat;
                    case OslTokens.INTTYPE: return TypeDescs.TypeInt;
                    case OslTokens.MATRIXTYPE: return TypeDescs.TypeMatrix;
                    case OslTokens.NORMALTYPE: return TypeDescs.TypeNormal;
                    case OslTokens.POINTTYPE: return TypeDescs.TypePoint;
                    case OslTokens.STRINGTYPE: return TypeDescs.TypeString;
                    case OslTokens.VECTORTYPE: return TypeDescs.TypeVector;
                    case OslTokens.VOIDTYPE: return new TypeDesc(TypeDesc.NONE);
                    default: return new TypeDesc(TypeDesc.UNKNOWN);
                }
            }

            internal void yyerror(string err)
            {
                errorfmt(filename(), lineno(), "Syntax error: " + err);
            }

            private sealed class LexerHost : IOslLexerHost
            {
                private OSLCompilerImpl c;

                public LexerHost(OSLCompilerImpl c)
                {
                    this.c = c;
                }

                public int lineno() { return c.lineno(); }
                public void lineno(int line) { c.lineno(line); }
                public void incr_lineno() { c.incr_lineno(); }
                public string filename() { return c.filename().@string(); }
                public void filename(string name) { c.filename(new ustring(name)); }
                public string main_filename() { return c.main_filename().@string(); }
                public string cwd() { return c.cwd(); }
                public void errorfmt(string file, int line, string message) { c.errorfmt(new ustring(file), line, message); }
                public void warningfmt(string file, int line, string message) { c.warningfmt(new ustring(file), line, message); }
                public void pragma_nowarn() { c.pragma_nowarn(); }
            }

            private static readonly object oslcompiler_mutex = new object();

            public bool osl_parse_buffer(string preprocessed_buffer)
            {
                // Thread safety with the lexer/parser
                lock (oslcompiler_mutex)
                {
                    OslLexer scanner = new OslLexer(preprocessed_buffer, new LexerHost(this));
                    oslparse(scanner);
                    return error_encountered();
                }
            }

            private const int YYEMPTY = -2;
            private const int YYEOF = 0;

            /// bison's yyparse. Returns 0 on success, 1 on a syntax error.
            private int oslparse(OslLexer scanner)
            {
                short[] yytranslate = OslGramTables.yytranslate, yyr1 = OslGramTables.yyr1, yyr2 = OslGramTables.yyr2;
                short[] yypact = OslGramTables.yypact, yydefact = OslGramTables.yydefact, yypgoto = OslGramTables.yypgoto;
                short[] yydefgoto = OslGramTables.yydefgoto, yytable = OslGramTables.yytable, yycheck = OslGramTables.yycheck;

                List<int> yyss = new List<int>();
                List<YYSTYPE> yyvs = new List<YYSTYPE>();
                List<YYLTYPE> yyls = new List<YYLTYPE>();

                int yystate = 0;
                int yychar = YYEMPTY;
                YYSTYPE yylval = new YYSTYPE();
                YYLTYPE yylloc = new YYLTYPE();
                yylloc.first_line = yylloc.last_line = 1;

                // The initial state, and value / location
                yyss.Add(0);
                yyvs.Add(new YYSTYPE());
                yyls.Add(yylloc);

                int yyn;
                int yytoken = 0;
                while (true)
                {
                    // yynewstate / yysetstate
                    if (yystate == OslGramTables.YYFINAL)
                    {
                        return 0;  // YYACCEPT
                    }

                    // yybackup
                    yyn = yypact[yystate];
                    bool reduce = false;
                    if (yyn != OslGramTables.YYPACT_NINF)
                    {
                        if (yychar == YYEMPTY)
                        {
                            yychar = scanner.osllex(out yylval, ref yylloc);
                        }
                        if (yychar <= YYEOF)
                        {
                            yychar = YYEOF;
                            yytoken = 0;
                        }
                        else
                        {
                            yytoken = (0 <= yychar && yychar <= OslGramTables.YYMAXUTOK) ? yytranslate[yychar] : 2;  // 2 = YYSYMBOL_YYUNDEF
                        }

                        yyn += yytoken;
                        if (yyn < 0 || OslGramTables.YYLAST < yyn || yycheck[yyn] != yytoken)
                        {
                            yyn = yydefact[yystate];
                            if (yyn == 0)
                            {
                                yyerror("syntax error");
                                return 1;  // YYABORT: no rule of the grammar has an error token to recover to
                            }
                            reduce = true;
                        }
                        else
                        {
                            yyn = yytable[yyn];
                            if (yyn <= 0)
                            {
                                if (yyn == OslGramTables.YYTABLE_NINF)
                                {
                                    yyerror("syntax error");
                                    return 1;
                                }
                                yyn = -yyn;
                                reduce = true;
                            }
                            else
                            {
                                // Shift the lookahead token
                                yystate = yyn;
                                yyss.Add(yystate);
                                yyvs.Add(yylval);
                                yyls.Add(yylloc);
                                yychar = YYEMPTY;
                                continue;
                            }
                        }
                    }
                    else
                    {
                        // yydefault
                        yyn = yydefact[yystate];
                        if (yyn == 0)
                        {
                            yyerror("syntax error");
                            return 1;
                        }
                        reduce = true;
                    }

                    if (reduce)
                    {
                        // yyreduce
                        int yylen = yyr2[yyn];
                        int top = yyvs.Count - 1;  // index of the symbol on top of the stack
                        YYSTYPE yyval = yylen > 0 ? yyvs[top + 1 - yylen] : new YYSTYPE();
                        YYLTYPE yyloc = new YYLTYPE();
                        if (yylen != 0)
                        {
                            yyloc.first_line = yyls[top + 1 - yylen].first_line;
                            yyloc.last_line = yyls[top].last_line;
                        }
                        else
                        {
                            yyloc.first_line = yyloc.last_line = yyls[top].last_line;
                        }

                        reduce_action(yyn, yylen, yyvs, yyls, top, ref yyval, yylloc);

                        // Pop the right-hand side, push the result
                        yyss.RemoveRange(yyss.Count - yylen, yylen);
                        yyvs.RemoveRange(yyvs.Count - yylen, yylen);
                        yyls.RemoveRange(yyls.Count - yylen, yylen);
                        yyvs.Add(yyval);
                        yyls.Add(yyloc);

                        // Now 'shift' the result of the reduction: the goto state
                        yyn = yyr1[yyn];
                        int top_state = yyss[yyss.Count - 1];
                        yystate = yypgoto[yyn - OslGramTables.YYNTOKENS] + top_state;
                        if (0 <= yystate && yystate <= OslGramTables.YYLAST && yycheck[yystate] == top_state)
                        {
                            yystate = yytable[yystate];
                        }
                        else
                        {
                            yystate = yydefgoto[yyn - OslGramTables.YYNTOKENS];
                        }
                        yyss.Add(yystate);
                    }
                }
            }

            private void reduce_action(int yyn, int yylen, List<YYSTYPE> vs, List<YYLTYPE> ls, int top, ref YYSTYPE yyval, YYLTYPE yylloc)
            {
                // $k and @k of a rule: counted from the symbol that begins the right-hand side. A mid-rule action sees only the symbols that
                // precede it, and so the offset in those cases is the number of preceding symbols (yylen is 0 for the empty $@n rule).
                int len = yylen;
                OSLCompilerImpl oslcompiler = this;

                switch (yyn)
                {
                    // global_declarations_opt: %empty
                    case 4:
                        yyval.n = null;
                        break;

                    // global_declarations: global_declarations global_declaration
                    case 6:
                        yyval.n = ASTNode.concat(vs[top + 1 - len].n, vs[top + 2 - len].n);
                        break;

                    // global_declaration: shader_or_function_declaration
                    case 7:
                        yyval.n = null;
                        break;

                    // global_declaration: struct_declaration
                    case 8:
                        yyval.n = null;
                        break;

                    // $@1: after "typespec_or_shadertype IDENTIFIER"
                    case 9:
                        len = 2;
                        if (vs[top + 1 - len].i == (int)ShaderType.Unknown)
                        {
                            // It's a function declaration, not a shader
                            oslcompiler.symtab().push();  // new scope
                            oslcompiler.typespec_stack().Push(oslcompiler.current_typespec());
                        }
                        break;

                    // $@2: after "... metadata_block_opt '('"
                    case 10:
                        len = 5;
                        if (vs[top + 1 - len].i != (int)ShaderType.Unknown)
                        {
                            oslcompiler.declaring_shader_formals(true);
                        }
                        break;

                    // $@3: after "... formal_params_opt ')'"
                    case 11:
                        oslcompiler.declaring_shader_formals(false);
                        break;

                    // shader_or_function_declaration: typespec_or_shadertype IDENTIFIER $@1 metadata_block_opt '(' $@2 formal_params_opt ')' $@3
                    //                                 metadata_block_opt function_body_or_just_decl
                    case 12:
                        {
                            int t1 = vs[top + 1 - len].i;
                            string id2 = vs[top + 2 - len].s;
                            ASTNode meta4 = vs[top + 4 - len].n;
                            ASTNode formals7 = vs[top + 7 - len].n;
                            ASTNode meta10 = vs[top + 10 - len].n;
                            ASTNode body11 = vs[top + 11 - len].n;
                            if (t1 == (int)ShaderType.Unknown)
                            {
                                // Function declaration
                                oslcompiler.symtab().pop();  // restore scope
                                ASTfunction_declaration f = new ASTfunction_declaration(oslcompiler, oslcompiler.typespec_stack().Peek(),
                                                                                        new ustring(id2), formals7, body11, null /*metadata*/,
                                                                                        ls[top + 2 - len].first_line);
                                oslcompiler.remember_function_decl(f);
                                f.add_meta(ASTNode.concat(meta4, meta10));
                                yyval.n = f;
                                oslcompiler.typespec_stack().Pop();
                            }
                            else
                            {
                                // Shader declaration
                                yyval.n = new ASTshader_declaration(oslcompiler, t1, new ustring(id2), formals7, body11, ASTNode.concat(meta4, meta10));
                                yyval.n.sourceline(ls[top + 2 - len].first_line);
                                if (oslcompiler.shader_is_defined())
                                {
                                    oslcompiler.yyerror("Only one shader is allowed per file.");
                                    yyval.n = null;
                                }
                                else
                                {
                                    oslcompiler.shader(yyval.n);
                                }
                            }
                        }
                        break;

                    // formal_params_opt: %empty
                    case 14:
                        yyval.n = null;
                        break;

                    // formal_params: formal_params ',' formal_param
                    case 16:
                        yyval.n = ASTNode.concat(vs[top + 1 - len].n, vs[top + 3 - len].n);
                        break;

                    // formal_params: formal_params ','
                    case 17:
                        yyval.n = vs[top + 1 - len].n;
                        break;

                    // formal_param: outputspec typespec IDENTIFIER initializer_opt metadata_block_opt
                    case 18:
                        {
                            TypeSpec t = oslcompiler.current_typespec();
                            bool is_output = vs[top + 1 - len].i != 0;
                            ASTvariable_declaration var = new ASTvariable_declaration(oslcompiler, t, new ustring(vs[top + 3 - len].s),
                                                                                      vs[top + 4 - len].n /*init*/,
                                                                                      oslcompiler.declaring_shader_formals() /*isparam*/,
                                                                                      false /*ismeta*/, is_output, false /*instlist*/,
                                                                                      ls[top + 3 - len].first_line);
                            if (!oslcompiler.declaring_shader_formals() && !is_output)
                            {
                                // these are function formals, not shader formals,
                                var.sym().readonly_(true);
                            }
                            var.add_meta(vs[top + 5 - len].n);
                            yyval.n = var;
                        }
                        break;

                    // formal_param: outputspec typespec IDENTIFIER arrayspec initializer_list_opt metadata_block_opt
                    case 19:
                        {
                            // Grab the current declaration type, modify it to be array
                            TypeSpec t = oslcompiler.current_typespec();
                            t.make_array(vs[top + 4 - len].i);
                            ASTvariable_declaration var = new ASTvariable_declaration(oslcompiler, t, new ustring(vs[top + 3 - len].s),
                                                                                      vs[top + 5 - len].n /*init*/,
                                                                                      oslcompiler.declaring_shader_formals() /*isparam*/,
                                                                                      false /*ismeta*/, vs[top + 1 - len].i != 0 /*isoutput*/,
                                                                                      true /* initlist */, ls[top + 3 - len].first_line);
                            var.add_meta(vs[top + 6 - len].n);
                            yyval.n = var;
                        }
                        break;

                    // formal_param: outputspec typespec IDENTIFIER initializer_list_opt metadata_block_opt
                    case 20:
                        {
                            TypeSpec t = oslcompiler.current_typespec();
                            if (!t.is_structure() && !t.is_triple() && !t.is_matrix())
                            {
                                oslcompiler.errorfmt(oslcompiler.filename(), oslcompiler.lineno(),
                                                     "Can't use '= {...}' initializer except with arrays, structs, vectors, or matrix ("
                                                         + vs[top + 3 - len].s + ")");
                            }
                            ASTvariable_declaration var = new ASTvariable_declaration(oslcompiler, t, new ustring(vs[top + 3 - len].s),
                                                                                      vs[top + 4 - len].n /*init*/,
                                                                                      oslcompiler.declaring_shader_formals() /*isparam*/,
                                                                                      false /*ismeta*/, vs[top + 1 - len].i != 0 /*isoutput*/,
                                                                                      true /* initializer list */, ls[top + 3 - len].first_line);
                            var.add_meta(vs[top + 5 - len].n);
                            yyval.n = var;
                        }
                        break;

                    // metadata_block_opt: METADATA_BEGIN metadata ']' ']'
                    case 21:
                        yyval.n = vs[top + 2 - len].n;
                        break;

                    // metadata_block_opt: %empty
                    case 22:
                        yyval.n = null;
                        break;

                    // metadata: metadata ',' metadatum
                    case 24:
                        yyval.n = ASTNode.concat(vs[top + 1 - len].n, vs[top + 3 - len].n);
                        break;

                    // metadata: metadata ','
                    case 25:
                        yyval.n = vs[top + 1 - len].n;
                        break;

                    // metadatum: simple_typename IDENTIFIER initializer
                    case 26:
                        yyval.n = new ASTvariable_declaration(oslcompiler, new TypeSpec(osllextype(vs[top + 1 - len].i)), new ustring(vs[top + 2 - len].s),
                                                              vs[top + 3 - len].n, false /* isparam */, true /* ismeta */, false /* isoutput */,
                                                              false /* initlist */, ls[top + 2 - len].first_line);
                        break;

                    // metadatum: simple_typename IDENTIFIER arrayspec initializer_list
                    case 27:
                        {
                            TypeDesc simple = osllextype(vs[top + 1 - len].i);
                            simple.arraylen = vs[top + 3 - len].i;
                            if (simple.arraylen < 1)
                            {
                                oslcompiler.errorfmt(oslcompiler.filename(), oslcompiler.lineno(), "Invalid array length for " + vs[top + 2 - len].s);
                            }
                            TypeSpec t = new TypeSpec(simple, false);
                            yyval.n = new ASTvariable_declaration(oslcompiler, t, new ustring(vs[top + 2 - len].s), vs[top + 4 - len].n, false,
                                                                  true /* ismeata */, false /* output */, true /* initializer list */,
                                                                  ls[top + 2 - len].first_line);
                        }
                        break;

                    // function_body_or_just_decl: '{' statement_list '}'
                    case 28:
                        yyval.n = vs[top + 2 - len].n;
                        break;

                    // function_body_or_just_decl: ';'
                    case 29:
                        yyval.n = null;
                        break;

                    // $@4: after "typespec IDENTIFIER"
                    case 30:
                        oslcompiler.symtab().push();  // new scope
                        oslcompiler.typespec_stack().Push(oslcompiler.current_typespec());
                        break;

                    // function_declaration: typespec IDENTIFIER $@4 '(' formal_params_opt ')' metadata_block_opt function_body_or_just_decl
                    case 31:
                        {
                            oslcompiler.symtab().pop();  // restore scope
                            ASTfunction_declaration f = new ASTfunction_declaration(oslcompiler, oslcompiler.typespec_stack().Peek(),
                                                                                    new ustring(vs[top + 2 - len].s), vs[top + 5 - len].n,
                                                                                    vs[top + 8 - len].n, null, ls[top + 2 - len].first_line);
                            oslcompiler.remember_function_decl(f);
                            f.add_meta(vs[top + 7 - len].n);
                            yyval.n = f;
                            oslcompiler.typespec_stack().Pop();
                        }
                        break;

                    // $@5: after "STRUCT IDENTIFIER '{'"
                    case 32:
                        {
                            len = 3;
                            ustring name = new ustring(vs[top + 2 - len].s);
                            Symbol s = oslcompiler.symtab().clash(name);
                            if (s != null)
                            {
                                oslcompiler.errorfmt(oslcompiler.filename(), oslcompiler.lineno(), "\"" + name + "\" already declared in this scope");
                                // FIXME -- print the file and line of the other definition
                            }
                            if (name.@string().StartsWith("___", StringComparison.Ordinal))
                            {
                                oslcompiler.errorfmt(oslcompiler.filename(), oslcompiler.lineno(),
                                                     "\"" + name + "\" : sorry, can't start with three underscores");
                            }
                            oslcompiler.symtab().new_struct(name);
                        }
                        break;

                    // struct_declaration: STRUCT IDENTIFIER '{' $@5 field_declarations '}' ';'
                    case 33:
                        yyval.n = null;
                        break;

                    // typed_field: IDENTIFIER
                    case 39:
                        {
                            ustring name = new ustring(vs[top + 1 - len].s);
                            TypeSpec t = oslcompiler.current_typespec();
                            StructSpec s = oslcompiler.symtab().current_struct();
                            if (s.lookup_field(name) >= 0)
                            {
                                oslcompiler.errorfmt(oslcompiler.filename(), oslcompiler.lineno(),
                                                     "Field \"" + name + "\" already exists in struct \"" + s.name() + "\"");
                            }
                            else
                            {
                                oslcompiler.symtab().add_struct_field(t, name);
                            }
                            yyval.n = null;
                        }
                        break;

                    // typed_field: IDENTIFIER arrayspec
                    case 40:
                        {
                            // Grab the current declaration type, modify it to be array
                            ustring name = new ustring(vs[top + 1 - len].s);
                            TypeSpec t = oslcompiler.current_typespec();
                            t.make_array(vs[top + 2 - len].i);
                            if (t.arraylength() < 1)
                            {
                                oslcompiler.errorfmt(oslcompiler.filename(), oslcompiler.lineno(), "Invalid array length for " + name);
                            }
                            StructSpec s = oslcompiler.symtab().current_struct();
                            if (s.lookup_field(name) >= 0)
                            {
                                oslcompiler.errorfmt(oslcompiler.filename(), oslcompiler.lineno(),
                                                     "Field \"" + name + "\" already exists in struct \"" + s.name() + "\"");
                            }
                            else
                            {
                                oslcompiler.symtab().add_struct_field(t, name);
                            }
                            yyval.n = null;
                        }
                        break;

                    // variable_declaration: typespec def_expressions ';'
                    case 43:
                        yyval.n = vs[top + 2 - len].n;
                        break;

                    // def_expressions: def_expressions ',' def_expression
                    case 45:
                        yyval.n = ASTNode.concat(vs[top + 1 - len].n, vs[top + 3 - len].n);
                        break;

                    // def_expression: IDENTIFIER initializer_opt
                    case 46:
                        {
                            TypeSpec t = oslcompiler.current_typespec();
                            yyval.n = new ASTvariable_declaration(oslcompiler, t, new ustring(vs[top + 1 - len].s), vs[top + 2 - len].n, false /*isparam*/,
                                                                  false /*ismeta*/, false /*isoutput*/, false /*initlist*/,
                                                                  ls[top + 1 - len].first_line);
                        }
                        break;

                    // def_expression: IDENTIFIER arrayspec initializer_list_opt
                    case 47:
                        {
                            // Grab the current declaration type, modify it to be array
                            TypeSpec t = oslcompiler.current_typespec();
                            t.make_array(vs[top + 2 - len].i);
                            if (vs[top + 2 - len].i < 1)
                            {
                                oslcompiler.errorfmt(oslcompiler.filename(), oslcompiler.lineno(), "Invalid array length for " + vs[top + 1 - len].s);
                            }
                            yyval.n = new ASTvariable_declaration(oslcompiler, t, new ustring(vs[top + 1 - len].s), vs[top + 3 - len].n, false, false, false,
                                                                  true /* initlist */, ls[top + 1 - len].first_line);
                        }
                        break;

                    // def_expression: IDENTIFIER initializer_list
                    case 48:
                        {
                            TypeSpec t = oslcompiler.current_typespec();
                            if (!t.is_structure() && !t.is_triple() && !t.is_matrix())
                            {
                                oslcompiler.errorfmt(oslcompiler.filename(), oslcompiler.lineno(),
                                                     "Can't use '= {...}' initializer except with arrays, struct, vectors, or matrix ("
                                                         + vs[top + 1 - len].s + ")");
                            }
                            yyval.n = new ASTvariable_declaration(oslcompiler, t, new ustring(vs[top + 1 - len].s), vs[top + 2 - len].n, false, false, false,
                                                                  true /* initlist */, ls[top + 1 - len].first_line);
                        }
                        break;

                    // initializer_opt: %empty
                    case 50:
                        yyval.n = null;
                        break;

                    // initializer: '=' expression
                    case 51:
                        yyval.n = vs[top + 2 - len].n;
                        break;

                    // initializer_list_opt: %empty
                    case 53:
                        yyval.n = null;
                        break;

                    // initializer_list: '=' compound_initializer
                    case 54:
                        yyval.n = vs[top + 2 - len].n;
                        break;

                    // compound_initializer: '{' init_expression_list '}'
                    case 55:
                        yyval.n = new ASTcompound_initializer(oslcompiler, vs[top + 2 - len].n);
                        yyval.n.sourceline(ls[top + 1 - len].first_line);
                        break;

                    // compound_initializer: '{' '}'
                    case 56:
                        if (!oslcompiler.declaring_shader_formals())
                        {
                            oslcompiler.errorfmt(oslcompiler.filename(), oslcompiler.lineno(),
                                                 "Empty compound initializers '{ }' only allowed for shader parameters.");
                        }
                        yyval.n = new ASTcompound_initializer(oslcompiler, null);
                        yyval.n.sourceline(ls[top + 1 - len].first_line);
                        break;

                    // init_expression_list: init_expression_list_rev ',' init_expression
                    case 58:
                        {
                            // The running list is accumulated in reverse (prepending is O(1)) and reversed once at the end
                            ASTNode revlist = ASTNode.concat(vs[top + 3 - len].n, vs[top + 1 - len].n);
                            revlist = ASTNode.reverse(revlist);
                            yyval.n = revlist;
                        }
                        break;

                    // init_expression_list_rev: init_expression_list_rev ',' init_expression
                    case 60:
                        // NOTE: intentionally concat in reverse order, because this is init_expression_list_rev!
                        yyval.n = ASTNode.concat(vs[top + 3 - len].n, vs[top + 1 - len].n);
                        break;

                    // outputspec: OUTPUT
                    case 63:
                        oslcompiler.current_output(true);
                        yyval.i = 1;
                        break;

                    // outputspec: %empty
                    case 64:
                        oslcompiler.current_output(false);
                        yyval.i = 0;
                        break;

                    // arrayspec: '[' INT_LITERAL ']'
                    case 74:
                        if (vs[top + 2 - len].i < 1)
                        {
                            oslcompiler.errorfmt(oslcompiler.filename(), oslcompiler.lineno(), "Invalid array length (" + vs[top + 2 - len].i + ")");
                        }
                        yyval.i = vs[top + 2 - len].i;
                        break;

                    // arrayspec: '[' ']'
                    case 75:
                        yyval.i = -1;
                        break;

                    // typespec: simple_typename
                    case 76:
                        oslcompiler.current_typespec(new TypeSpec(osllextype(vs[top + 1 - len].i)));
                        yyval.i = 0;
                        break;

                    // typespec: CLOSURE simple_typename
                    case 77:
                        oslcompiler.current_typespec(new TypeSpec(osllextype(vs[top + 2 - len].i), true));
                        yyval.i = 0;
                        break;

                    // typespec: IDENTIFIER  (struct name)
                    case 78:
                        {
                            ustring name = new ustring(vs[top + 1 - len].s);
                            Symbol s = oslcompiler.symtab().find(name);
                            if (s != null && s.is_structure())
                            {
                                oslcompiler.current_typespec(new TypeSpec("", s.typespec().structure()));
                            }
                            else
                            {
                                oslcompiler.current_typespec(new TypeSpec(new TypeDesc(TypeDesc.UNKNOWN)));
                                oslcompiler.errorfmt(oslcompiler.filename(), oslcompiler.lineno(), "Unknown struct name: " + vs[top + 1 - len].s);
                            }
                            yyval.i = 0;
                        }
                        break;

                    // typespec_or_shadertype: simple_typename
                    case 79:
                        oslcompiler.current_typespec(new TypeSpec(osllextype(vs[top + 1 - len].i)));
                        yyval.i = 0;
                        break;

                    // typespec_or_shadertype: CLOSURE simple_typename
                    case 80:
                        oslcompiler.current_typespec(new TypeSpec(osllextype(vs[top + 2 - len].i), true));
                        yyval.i = 0;
                        break;

                    // typespec_or_shadertype: IDENTIFIER  (struct name or shader type name)
                    case 81:
                        {
                            // N.B. Shader types are considered obsolete. We are promoting 'shader' for all, now (OSL 2.0).
                            string name = vs[top + 1 - len].s;
                            if (name == "shader")
                            {
                                yyval.i = (int)ShaderType.Generic;
                            }
                            else if (name == "surface")
                            {
                                yyval.i = (int)ShaderType.Surface;
                            }
                            else if (name == "displacement")
                            {
                                yyval.i = (int)ShaderType.Displacement;
                            }
                            else if (name == "volume")
                            {
                                yyval.i = (int)ShaderType.Volume;
                            }
                            else
                            {
                                Symbol s = oslcompiler.symtab().find(new ustring(name));
                                if (s != null && s.is_structure())
                                {
                                    oslcompiler.current_typespec(new TypeSpec("", s.typespec().structure()));
                                }
                                else
                                {
                                    oslcompiler.current_typespec(new TypeSpec(new TypeDesc(TypeDesc.UNKNOWN)));
                                    oslcompiler.errorfmt(oslcompiler.filename(), oslcompiler.lineno(), "Unknown struct name: " + name);
                                }
                                yyval.i = (int)ShaderType.Unknown;
                            }
                        }
                        break;

                    // statement_list: statement statement_list
                    case 82:
                        yyval.n = ASTNode.concat(vs[top + 1 - len].n, vs[top + 2 - len].n);
                        break;

                    // statement_list: %empty
                    case 83:
                        yyval.n = null;
                        break;

                    // statement: ';'
                    case 91:
                        yyval.n = null;
                        break;

                    // $@6: after "'{'"
                    case 92:
                        oslcompiler.symtab().push();  // new scope
                        break;

                    // scoped_statements: '{' $@6 statement_list '}'
                    case 93:
                        oslcompiler.symtab().pop();  // restore scope
                        yyval.n = vs[top + 3 - len].n;
                        break;

                    // conditional_statement: IF_TOKEN '(' compound_expression ')' statement
                    case 94:
                        yyval.n = new ASTconditional_statement(oslcompiler, vs[top + 3 - len].n, vs[top + 5 - len].n);
                        yyval.n.sourceline(ls[top + 1 - len].first_line);
                        break;

                    // conditional_statement: IF_TOKEN '(' compound_expression ')' statement ELSE statement
                    case 95:
                        yyval.n = new ASTconditional_statement(oslcompiler, vs[top + 3 - len].n, vs[top + 5 - len].n, vs[top + 7 - len].n);
                        yyval.n.sourceline(ls[top + 1 - len].first_line);
                        break;

                    // loop_statement: WHILE '(' compound_expression ')' statement
                    case 96:
                        yyval.n = new ASTloop_statement(oslcompiler, ASTloop_statement.LoopWhile, null, vs[top + 3 - len].n, null, vs[top + 5 - len].n);
                        yyval.n.sourceline(ls[top + 1 - len].first_line);
                        break;

                    // loop_statement: DO statement WHILE '(' compound_expression ')' ';'
                    case 97:
                        yyval.n = new ASTloop_statement(oslcompiler, ASTloop_statement.LoopDo, null, vs[top + 5 - len].n, null, vs[top + 2 - len].n);
                        yyval.n.sourceline(ls[top + 1 - len].first_line);
                        break;

                    // $@7: after "FOR '('"
                    case 98:
                        oslcompiler.symtab().push();  // new declaration scope
                        break;

                    // loop_statement: FOR '(' $@7 for_init_statement compound_expression_opt ';' compound_expression_opt ')' statement
                    case 99:
                        yyval.n = new ASTloop_statement(oslcompiler, ASTloop_statement.LoopFor, vs[top + 4 - len].n, vs[top + 5 - len].n,
                                                        vs[top + 7 - len].n, vs[top + 9 - len].n);
                        yyval.n.sourceline(ls[top + 1 - len].first_line);
                        oslcompiler.symtab().pop();
                        break;

                    // loopmod_statement: BREAK ';'
                    case 100:
                        yyval.n = new ASTloopmod_statement(oslcompiler, ASTloopmod_statement.LoopModBreak);
                        break;

                    // loopmod_statement: CONTINUE ';'
                    case 101:
                        yyval.n = new ASTloopmod_statement(oslcompiler, ASTloopmod_statement.LoopModContinue);
                        break;

                    // return_statement: RETURN expression_opt ';'
                    case 102:
                        yyval.n = new ASTreturn_statement(oslcompiler, vs[top + 2 - len].n);
                        break;

                    // return_statement: RETURN compound_initializer ';'
                    case 103:
                        yyval.n = new ASTreturn_statement(oslcompiler, vs[top + 2 - len].n);
                        break;

                    // expression_list: expression_list ',' expression
                    case 107:
                        yyval.n = ASTNode.concat(vs[top + 1 - len].n, vs[top + 3 - len].n);
                        break;

                    // expression_opt: %empty
                    case 109:
                        yyval.n = null;
                        break;

                    // compound_expression_opt: %empty
                    case 111:
                        yyval.n = null;
                        break;

                    // compound_expression: expression ',' compound_expression
                    case 113:
                        yyval.n = new ASTcomma_operator(oslcompiler, ASTNode.concat(vs[top + 1 - len].n, vs[top + 3 - len].n));
                        break;

                    // expression: INT_LITERAL
                    case 114:
                        yyval.n = new ASTliteral(oslcompiler, vs[top + 1 - len].i);
                        break;

                    // expression: FLOAT_LITERAL
                    case 115:
                        yyval.n = new ASTliteral(oslcompiler, vs[top + 1 - len].f);
                        break;

                    // expression: string_literal_group
                    case 116:
                        yyval.n = new ASTliteral(oslcompiler, new ustring(vs[top + 1 - len].s));
                        break;

                    // expression: incdec_op variable_lvalue
                    case 118:
                        yyval.n = new ASTpreincdec(oslcompiler, vs[top + 1 - len].i, vs[top + 2 - len].n);
                        break;

                    // expression: unary_op expression %prec UMINUS_PREC
                    case 120:
                        {
                            int op1 = vs[top + 1 - len].i;
                            ASTNode e2 = vs[top + 2 - len].n;
                            // Correct for literal +float -float
                            if (op1 == ASTNode.Add)
                            {
                                yyval.n = e2;
                            }
                            else if (op1 == ASTNode.Sub && e2.nodetype() == ASTNode.NodeType.literal_node && e2.typespec().is_numeric())
                            {
                                ((ASTliteral)e2).negate();
                                yyval.n = e2;
                            }
                            else
                            {
                                yyval.n = new ASTunary_expression(oslcompiler, op1, e2);
                                yyval.n.sourceline(ls[top + 1 - len].first_line);
                            }
                        }
                        break;

                    // expression: '(' compound_expression ')'
                    case 121:
                        {
                            ASTNode e2 = vs[top + 2 - len].n;
                            if (e2.nodetype() == ASTNode.NodeType.comma_operator_node)
                            {
                                // Warning for comma operator in parenthesized expressions, which sometimes happens when somebody forgets the proper
                                // syntax for triple constructors:  color x = Cd * (a, b, c);  when they really meant  color(a, b, c).
                                oslcompiler.warningfmt(oslcompiler.filename(), ls[top + 1 - len].first_line,
                                                       "Comma operator inside parenthesis is probably an error -- it is not a vector/color.");
                            }
                            yyval.n = e2;
                        }
                        break;

                    // variable_lvalue: id_or_field '[' expression ']'
                    case 128:
                        yyval.n = new ASTindex(oslcompiler, vs[top + 1 - len].n, vs[top + 3 - len].n);
                        yyval.n.sourceline(ls[top + 2 - len].first_line);
                        break;

                    // variable_lvalue: id_or_field '[' expression ']' '[' expression ']'
                    case 129:
                        yyval.n = new ASTindex(oslcompiler, vs[top + 1 - len].n, vs[top + 3 - len].n, vs[top + 6 - len].n);
                        yyval.n.sourceline(ls[top + 2 - len].first_line);
                        break;

                    // variable_lvalue: id_or_field '[' expression ']' '[' expression ']' '[' expression ']'
                    case 130:
                        yyval.n = new ASTindex(oslcompiler, vs[top + 1 - len].n, vs[top + 3 - len].n, vs[top + 6 - len].n, vs[top + 9 - len].n);
                        yyval.n.sourceline(ls[top + 2 - len].first_line);
                        break;

                    // id_or_field: IDENTIFIER
                    case 131:
                        yyval.n = new ASTvariable_ref(oslcompiler, new ustring(vs[top + 1 - len].s));
                        break;

                    // id_or_field: variable_lvalue '.' IDENTIFIER
                    case 132:
                        yyval.n = new ASTstructselect(oslcompiler, vs[top + 1 - len].n, new ustring(vs[top + 3 - len].s));
                        yyval.n.sourceline(ls[top + 2 - len].first_line);
                        break;

                    // variable_ref: variable_lvalue incdec_op_opt
                    case 133:
                        if (vs[top + 2 - len].i != 0)
                        {
                            yyval.n = new ASTpostincdec(oslcompiler, vs[top + 2 - len].i, vs[top + 1 - len].n);
                        }
                        else
                        {
                            yyval.n = vs[top + 1 - len].n;
                        }
                        break;

                    // binary_expression: expression <op> expression  (cases 134..151)
                    case 134: yyval.n = binary(ASTNode.Or, vs, ls, top, len); break;
                    case 135: yyval.n = binary(ASTNode.And, vs, ls, top, len); break;
                    case 136: yyval.n = binary(ASTNode.BitOr, vs, ls, top, len); break;
                    case 137: yyval.n = binary(ASTNode.Xor, vs, ls, top, len); break;
                    case 138: yyval.n = binary(ASTNode.BitAnd, vs, ls, top, len); break;
                    case 139: yyval.n = binary(ASTNode.Equal, vs, ls, top, len); break;
                    case 140: yyval.n = binary(ASTNode.NotEqual, vs, ls, top, len); break;
                    case 141: yyval.n = binary(ASTNode.Greater, vs, ls, top, len); break;
                    case 142: yyval.n = binary(ASTNode.GreaterEqual, vs, ls, top, len); break;
                    case 143: yyval.n = binary(ASTNode.Less, vs, ls, top, len); break;
                    case 144: yyval.n = binary(ASTNode.LessEqual, vs, ls, top, len); break;
                    case 145: yyval.n = binary(ASTNode.ShiftLeft, vs, ls, top, len); break;
                    case 146: yyval.n = binary(ASTNode.ShiftRight, vs, ls, top, len); break;
                    case 147: yyval.n = binary(ASTNode.Add, vs, ls, top, len); break;
                    case 148: yyval.n = binary(ASTNode.Sub, vs, ls, top, len); break;
                    case 149: yyval.n = binary(ASTNode.Mul, vs, ls, top, len); break;
                    case 150: yyval.n = binary(ASTNode.Div, vs, ls, top, len); break;
                    case 151: yyval.n = binary(ASTNode.Mod, vs, ls, top, len); break;

                    // unary_op: '-' | '+' | '!' | NOT_OP | '~'
                    case 152: yyval.i = ASTNode.Sub; break;
                    case 153: yyval.i = ASTNode.Add; break;
                    case 154: yyval.i = ASTNode.Not; break;
                    case 155: yyval.i = ASTNode.Not; break;
                    case 156: yyval.i = ASTNode.Compl; break;

                    // incdec_op_opt: %empty
                    case 158:
                        yyval.i = 0;
                        break;

                    // incdec_op: INCREMENT | DECREMENT
                    case 159:
                        yyval.i = ASTNode.Incr;
                        break;
                    case 160:
                        yyval.i = ASTNode.Decr;
                        break;

                    // type_constructor: simple_typename '(' expression_list ')'
                    case 161:
                        yyval.n = new ASTtype_constructor(oslcompiler, new TypeSpec(osllextype(vs[top + 1 - len].i)), vs[top + 3 - len].n);
                        yyval.n.sourceline(ls[top + 1 - len].first_line);
                        break;

                    // function_call: IDENTIFIER '(' function_args_opt ')'
                    case 162:
                        yyval.n = new ASTfunction_call(oslcompiler, new ustring(vs[top + 1 - len].s), vs[top + 3 - len].n);
                        yyval.n.sourceline(ls[top + 1 - len].first_line);
                        break;

                    // function_args_opt: %empty
                    case 164:
                        yyval.n = null;
                        break;

                    // function_args: function_args ',' expression | function_args ',' compound_initializer
                    case 167:
                    case 168:
                        yyval.n = ASTNode.concat(vs[top + 1 - len].n, vs[top + 3 - len].n);
                        break;

                    // assign_expression: variable_lvalue <assignop> expression  (cases 169..178)
                    case 169: yyval.n = assign(ASTNode.Assign, vs, ls, top, len); break;
                    case 170: yyval.n = assign(ASTNode.Mul, vs, ls, top, len); break;
                    case 171: yyval.n = assign(ASTNode.Div, vs, ls, top, len); break;
                    case 172: yyval.n = assign(ASTNode.Add, vs, ls, top, len); break;
                    case 173: yyval.n = assign(ASTNode.Sub, vs, ls, top, len); break;
                    case 174: yyval.n = assign(ASTNode.BitAnd, vs, ls, top, len); break;
                    case 175: yyval.n = assign(ASTNode.BitOr, vs, ls, top, len); break;
                    case 176: yyval.n = assign(ASTNode.Xor, vs, ls, top, len); break;
                    case 177: yyval.n = assign(ASTNode.ShiftLeft, vs, ls, top, len); break;
                    case 178: yyval.n = assign(ASTNode.ShiftRight, vs, ls, top, len); break;

                    // ternary_expression: expression '?' expression ':' expression
                    case 179:
                        yyval.n = new ASTternary_expression(oslcompiler, vs[top + 1 - len].n, vs[top + 3 - len].n, vs[top + 5 - len].n);
                        yyval.n.sourceline(ls[top + 2 - len].first_line);
                        break;

                    // typecast_expression: '(' simple_typename ')' expression
                    case 180:
                        yyval.n = new ASTtypecast_expression(oslcompiler, new TypeSpec(osllextype(vs[top + 2 - len].i)), vs[top + 4 - len].n);
                        break;

                    // string_literal_group: string_literal_group STRING_LITERAL
                    case 182:
                        yyval.s = new ustring(vs[top + 1 - len].s + vs[top + 2 - len].s).@string();
                        break;

                    default:
                        // The rest are the default action $$ = $1 (already in yyval)
                        break;
                }
            }

            private ASTNode binary(int op, List<YYSTYPE> vs, List<YYLTYPE> ls, int top, int len)
            {
                ASTNode n = ASTbinary_expression.make(this, op, vs[top + 1 - len].n, vs[top + 3 - len].n);
                n.sourceline(ls[top + 2 - len].first_line);
                return n;
            }

            private ASTNode assign(int op, List<YYSTYPE> vs, List<YYLTYPE> ls, int top, int len)
            {
                ASTNode n = new ASTassign_expression(this, vs[top + 1 - len].n, op, vs[top + 3 - len].n);
                n.sourceline(ls[top + 2 - len].first_line);
                return n;
            }
        }
    }
}
