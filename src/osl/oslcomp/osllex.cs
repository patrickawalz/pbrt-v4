// OSL port: liboslcomp/osllex.l
//
// The lexical scanner of the shader compiler. flex generates the scanner of the C++ from the rules of osllex.l; here the same rules are
// written out as code, with flex's choice of the longest match and, among equal lengths, of the earliest rule. The input is the text that
// the preprocessor has produced (comments of the form /* */ are already gone, and the lines "# 12 "file.osl"" tell the scanner where it is).

using System;
using System.Globalization;
using System.Text;

namespace OSL
{
    namespace pvt
    {

        // YYSTYPE Definition: the union of bison's %union { int i; float f; ASTNode *n; const char *s; } without the node
        public struct YYSTYPE
        {
            public int i;
            public float f;
            public string s;
            public ASTNode n;
        }

        // YYLTYPE Definition
        public struct YYLTYPE
        {
            public int first_line;
            public int last_line;
        }

        // IOslLexerHost Definition: what the scanner needs of OSLCompilerImpl
        public interface IOslLexerHost
        {
            int lineno();
            void lineno(int line);
            void incr_lineno();
            string filename();
            void filename(string name);
            string main_filename();
            string cwd();
            void errorfmt(string file, int line, string message);
            void warningfmt(string file, int line, string message);
            void pragma_nowarn();
        }

        // OslLexer Definition
        public sealed class OslLexer
        {
            // OslLexer Public Methods
            public OslLexer(string buffer, IOslLexerHost oslcompiler)
            {
                this.buffer = buffer;
                this.oslcompiler = oslcompiler;
                position = 0;
                atLineStart = true;
            }

            // osllex(): the next token (0 at the end of the input); yylval and yylloc are filled in as by the actions of the rules
            public int osllex(out YYSTYPE yylval, ref YYLTYPE yylloc)
            {
                yylval = new YYSTYPE();
                while (position < buffer.Length)
                {
                    int start = position;
                    char c = buffer[position];
                    bool lineStart = atLineStart;
                    atLineStart = false;

                    // {CPP}  ^[ \t]*#.*\n
                    if (lineStart)
                    {
                        int p = position;
                        while (p < buffer.Length && (buffer[p] == ' ' || buffer[p] == '\t'))
                        {
                            ++p;
                        }
                        if (p < buffer.Length && buffer[p] == '#')
                        {
                            int end = buffer.IndexOf('\n', p);
                            if (end >= 0)
                            {
                                string text = buffer.Substring(position, end + 1 - position);
                                position = end + 1;
                                atLineStart = true;
                                preprocess(text, ref yylloc);
                                SETLINE(ref yylloc);
                                continue;
                            }
                        }
                    }

                    // {CPLUSCOMMENT}  \/\/.*\n
                    if (c == '/' && position + 1 < buffer.Length && buffer[position + 1] == '/')
                    {
                        int end = buffer.IndexOf('\n', position);
                        if (end >= 0)
                        {
                            position = end + 1;
                            atLineStart = true;
                            oslcompiler.incr_lineno();
                            SETLINE(ref yylloc);
                            continue;
                        }
                    }

                    // identifiers, keywords and reserved words: the rules for the words come before {IDENT}, so a word wins at equal length
                    if (char.IsAsciiLetter(c) || c == '_')
                    {
                        int end = position + 1;
                        while (end < buffer.Length && (char.IsAsciiLetterOrDigit(buffer[end]) || buffer[end] == '_'))
                        {
                            ++end;
                        }
                        string word = buffer.Substring(position, end - position);
                        position = end;
                        int keyword = Keyword(word);
                        if (keyword != 0)
                        {
                            SETLINE(ref yylloc);
                            yylval.i = keyword;
                            return keyword;
                        }
                        if (IsReserved(word))
                        {
                            oslcompiler.errorfmt(oslcompiler.filename(), oslcompiler.lineno(), "'" + word + "' is a reserved word");
                            SETLINE(ref yylloc);
                            yylval.i = OslTokens.RESERVED;
                            return OslTokens.RESERVED;
                        }
                        yylval.s = string.Intern(word);
                        SETLINE(ref yylloc);
                        return OslTokens.IDENTIFIER;
                    }

                    // literal values: {HEXINTEGER}, {FLT}, {INTEGER}, the longest match wins
                    if (char.IsAsciiDigit(c) || (c == '.' && position + 1 < buffer.Length && char.IsAsciiDigit(buffer[position + 1])))
                    {
                        int hexEnd = MatchHex(position);
                        int fltEnd = MatchFloat(position);
                        int intEnd = MatchInteger(position);
                        if (hexEnd > fltEnd && hexEnd > intEnd)
                        {
                            string text = buffer.Substring(position, hexEnd - position);
                            position = hexEnd;
                            long llval = long.Parse(text.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
                            // we do not detect overflow when the value is INT_MAX+1, because negation happens later and -(INT_MAX+1) == INT_MIN
                            if (llval > (long)uint.MaxValue + 1)
                            {
                                oslcompiler.errorfmt(oslcompiler.filename(), oslcompiler.lineno(),
                                                     "integer overflow, value must be between " + int.MinValue + " and " + int.MaxValue + ".");
                            }
                            yylval.i = (int)llval;
                            SETLINE(ref yylloc);
                            return OslTokens.INT_LITERAL;
                        }
                        if (fltEnd > intEnd)
                        {
                            string text = buffer.Substring(position, fltEnd - position);
                            position = fltEnd;
                            yylval.f = float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
                            SETLINE(ref yylloc);
                            return OslTokens.FLOAT_LITERAL;
                        }
                        {
                            string text = buffer.Substring(position, intEnd - position);
                            position = intEnd;
                            long llval;
                            if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out llval))
                            {
                                llval = long.MaxValue;
                            }
                            if (llval > (long)int.MaxValue + 1)
                            {
                                oslcompiler.errorfmt(oslcompiler.filename(), oslcompiler.lineno(),
                                                     "integer overflow, value must be between " + int.MinValue + " and " + int.MaxValue + ".");
                            }
                            yylval.i = (int)llval;
                            SETLINE(ref yylloc);
                            return OslTokens.INT_LITERAL;
                        }
                    }

                    // {STR}  \"(\\.|[^\\"\n])*\"
                    if (c == '"')
                    {
                        int end = MatchString(position);
                        if (end > 0)
                        {
                            // grab the material between the quotes
                            string s = buffer.Substring(position + 1, end - position - 2);
                            position = end;
                            if (s.IndexOf('\\') >= 0)
                            {
                                s = unescape_chars(s);
                            }
                            yylval.s = string.Intern(s);
                            SETLINE(ref yylloc);
                            return OslTokens.STRING_LITERAL;
                        }
                    }

                    // the two- and three-character operators
                    int op = MatchOperator(position, out int length);
                    if (op != 0)
                    {
                        position += length;
                        SETLINE(ref yylloc);
                        yylval.i = op;
                        return op;
                    }

                    // "\\\n" | [\n]: end of line
                    if (c == '\n' || (c == '\\' && position + 1 < buffer.Length && buffer[position + 1] == '\n'))
                    {
                        position += c == '\n' ? 1 : 2;
                        atLineStart = true;
                        oslcompiler.incr_lineno();
                        SETLINE(ref yylloc);
                        continue;
                    }

                    // {WHITE}  [ \t\v\f\r]+
                    if (c == ' ' || c == '\t' || c == '\v' || c == '\f' || c == '\r')
                    {
                        while (position < buffer.Length && (buffer[position] == ' ' || buffer[position] == '\t' || buffer[position] == '\v' ||
                                                            buffer[position] == '\f' || buffer[position] == '\r'))
                        {
                            ++position;
                        }
                        // the line start is kept for the {CPP} rule only through the leading white space it matches itself
                        atLineStart = lineStart;
                        continue;
                    }

                    // catch-all rule for any other single characters
                    ++position;
                    SETLINE(ref yylloc);
                    if (c == '!')
                    {
                        yylval.i = OslTokens.NOT_OP;
                        return OslTokens.NOT_OP;
                    }
                    yylval.i = c;
                    return c;
                }
                return 0;
            }

            // OslLexer Private Methods
            private void SETLINE(ref YYLTYPE yylloc)
            {
                yylloc.first_line = yylloc.last_line = oslcompiler.lineno();
            }

            private static int Keyword(string word)
            {
                switch (word)
                {
                    case "break": return OslTokens.BREAK;
                    case "closure": return OslTokens.CLOSURE;
                    case "color": return OslTokens.COLORTYPE;
                    case "continue": return OslTokens.CONTINUE;
                    case "do": return OslTokens.DO;
                    case "else": return OslTokens.ELSE;
                    case "float": return OslTokens.FLOATTYPE;
                    case "for": return OslTokens.FOR;
                    case "if": return OslTokens.IF_TOKEN;
                    case "illuminance": return OslTokens.ILLUMINANCE;
                    case "illuminate": return OslTokens.ILLUMINATE;
                    case "int": return OslTokens.INTTYPE;
                    case "matrix": return OslTokens.MATRIXTYPE;
                    case "normal": return OslTokens.NORMALTYPE;
                    case "output": return OslTokens.OUTPUT;
                    case "point": return OslTokens.POINTTYPE;
                    case "public": return OslTokens.PUBLIC;
                    case "return": return OslTokens.RETURN;
                    case "string": return OslTokens.STRINGTYPE;
                    case "struct": return OslTokens.STRUCT;
                    case "vector": return OslTokens.VECTORTYPE;
                    case "void": return OslTokens.VOIDTYPE;
                    case "while": return OslTokens.WHILE;
                    case "or": return OslTokens.OR_OP;
                    case "and": return OslTokens.AND_OP;
                    case "not": return OslTokens.NOT_OP;
                }
                return 0;
            }

            private static bool IsReserved(string word)
            {
                switch (word)
                {
                    case "bool": case "case": case "char": case "class": case "const": case "default": case "double":
                    case "enum": case "extern": case "false": case "friend": case "inline": case "long": case "private":
                    case "protected": case "short": case "signed": case "sizeof": case "static":
                    case "switch": case "template": case "this": case "true": case "typedef": case "uniform":
                    case "union": case "unsigned": case "varying": case "virtual":
                        return true;
                }
                return false;
            }

            // The end of the longest match of 0[xX][0-9a-fA-F]+ at p (p if none)
            private int MatchHex(int p)
            {
                if (p + 2 < buffer.Length && buffer[p] == '0' && (buffer[p + 1] == 'x' || buffer[p + 1] == 'X') &&
                    char.IsAsciiHexDigit(buffer[p + 2]))
                {
                    int q = p + 2;
                    while (q < buffer.Length && char.IsAsciiHexDigit(buffer[q]))
                    {
                        ++q;
                    }
                    return q;
                }
                return p;
            }

            private int MatchInteger(int p)
            {
                int q = p;
                while (q < buffer.Length && char.IsAsciiDigit(buffer[q]))
                {
                    ++q;
                }
                return q;
            }

            // E  [eE][-+]?{DIGIT}+ at p: the end of the match, or p
            private int MatchExponent(int p)
            {
                if (p < buffer.Length && (buffer[p] == 'e' || buffer[p] == 'E'))
                {
                    int q = p + 1;
                    if (q < buffer.Length && (buffer[q] == '-' || buffer[q] == '+'))
                    {
                        ++q;
                    }
                    int digits = q;
                    while (q < buffer.Length && char.IsAsciiDigit(buffer[q]))
                    {
                        ++q;
                    }
                    if (q > digits)
                    {
                        return q;
                    }
                }
                return p;
            }

            // FLT  {FLT1}|{FLT2}|{FLT3}: the longest of  D+ . D* E?,  D* . D+ E?,  D+ E
            private int MatchFloat(int p)
            {
                int best = p;
                int q = p;
                while (q < buffer.Length && char.IsAsciiDigit(buffer[q]))
                {
                    ++q;
                }
                int integerDigits = q - p;
                if (integerDigits > 0)
                {
                    // FLT3: D+ E
                    int e = MatchExponent(q);
                    if (e > q)
                    {
                        best = Math.Max(best, e);
                    }
                }
                if (q < buffer.Length && buffer[q] == '.')
                {
                    int f = q + 1;
                    while (f < buffer.Length && char.IsAsciiDigit(buffer[f]))
                    {
                        ++f;
                    }
                    int fractionDigits = f - q - 1;
                    if (integerDigits > 0 || fractionDigits > 0)
                    {
                        // FLT1 (D+ . D* E?) and FLT2 (D* . D+ E?)
                        best = Math.Max(best, MatchExponent(f) > f ? MatchExponent(f) : f);
                    }
                }
                return best;
            }

            // The end of \"(\\.|[^\\"\n])*\" at p, or -1
            private int MatchString(int p)
            {
                int q = p + 1;
                while (q < buffer.Length)
                {
                    char c = buffer[q];
                    if (c == '"')
                    {
                        return q + 1;
                    }
                    if (c == '\n')
                    {
                        return -1;
                    }
                    if (c == '\\')
                    {
                        // \\. : the backslash and any character but the newline
                        if (q + 1 >= buffer.Length || buffer[q + 1] == '\n')
                        {
                            return -1;
                        }
                        q += 2;
                    }
                    else
                    {
                        ++q;
                    }
                }
                return -1;
            }

            private int MatchOperator(int p, out int length)
            {
                length = 0;
                char c = buffer[p];
                char d = p + 1 < buffer.Length ? buffer[p + 1] : '\0';
                char e = p + 2 < buffer.Length ? buffer[p + 2] : '\0';
                if (c == '<' && d == '<' && e == '=')
                {
                    length = 3;
                    return OslTokens.SHL_ASSIGN;
                }
                if (c == '>' && d == '>' && e == '=')
                {
                    length = 3;
                    return OslTokens.SHR_ASSIGN;
                }
                int two = 0;
                switch (c)
                {
                    case '+': two = d == '=' ? OslTokens.ADD_ASSIGN : (d == '+' ? OslTokens.INCREMENT : 0); break;
                    case '-': two = d == '=' ? OslTokens.SUB_ASSIGN : (d == '-' ? OslTokens.DECREMENT : 0); break;
                    case '*': two = d == '=' ? OslTokens.MUL_ASSIGN : 0; break;
                    case '/': two = d == '=' ? OslTokens.DIV_ASSIGN : 0; break;
                    case '&': two = d == '=' ? OslTokens.BIT_AND_ASSIGN : (d == '&' ? OslTokens.AND_OP : 0); break;
                    case '|': two = d == '=' ? OslTokens.BIT_OR_ASSIGN : (d == '|' ? OslTokens.OR_OP : 0); break;
                    case '^': two = d == '=' ? OslTokens.XOR_ASSIGN : 0; break;
                    case '<': two = d == '<' ? OslTokens.SHL_OP : (d == '=' ? OslTokens.LE_OP : 0); break;
                    case '>': two = d == '>' ? OslTokens.SHR_OP : (d == '=' ? OslTokens.GE_OP : 0); break;
                    case '=': two = d == '=' ? OslTokens.EQ_OP : 0; break;
                    case '!': two = d == '=' ? OslTokens.NE_OP : 0; break;
                    case '[': two = d == '[' ? OslTokens.METADATA_BEGIN : 0; break;
                }
                if (two != 0)
                {
                    length = 2;
                    return two;
                }
                return 0;
            }

            // OIIO::Strutil::unescape_chars: \n \t \v \b \r \f \a, \\ and \" (any other \c is c), and octal escapes of up to three digits
            private static string unescape_chars(string s)
            {
                StringBuilder sb = new StringBuilder(s.Length);
                for (int i = 0; i < s.Length; ++i)
                {
                    char c = s[i];
                    if (c != '\\' || i + 1 >= s.Length)
                    {
                        sb.Append(c);
                        continue;
                    }
                    ++i;
                    char n = s[i];
                    if (n >= '0' && n <= '7')
                    {
                        int value = 0, digits = 0;
                        while (i < s.Length && digits < 3 && s[i] >= '0' && s[i] <= '7')
                        {
                            value = value * 8 + (s[i] - '0');
                            ++i;
                            ++digits;
                        }
                        --i;
                        sb.Append((char)value);
                        continue;
                    }
                    switch (n)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'v': sb.Append('\v'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'a': sb.Append('\a'); break;
                        default: sb.Append(n); break;
                    }
                }
                return sb.ToString();
            }

            // preprocess(): a line that starts with # is a pragma, or the line number and file name that the preprocessor leaves
            private void preprocess(string str, ref YYLTYPE yylloc)
            {
                int p = 0;
                while (p < str.Length && (str[p] == ' ' || str[p] == '\t'))
                {
                    ++p;
                }
                if (p >= str.Length || str[p] != '#')
                {
                    oslcompiler.errorfmt(oslcompiler.filename(), oslcompiler.lineno(), "Possible bug in shader preprocess");
                    SETLINE(ref yylloc);
                    return;
                }
                ++p;
                while (p < str.Length && (str[p] == ' ' || str[p] == '\t'))
                {
                    ++p;
                }
                if (string.CompareOrdinal(str, p, "pragma", 0, 6) == 0)
                {
                    string line = str.Substring(p + 6);
                    int cursor = 0;
                    string pragmatype = ParseWord(line, ref cursor);
                    if (pragmatype == "error")
                    {
                        string msg;
                        if (ParseString(line, ref cursor, out msg))
                        {
                            oslcompiler.errorfmt(oslcompiler.filename(), oslcompiler.lineno(), msg);
                        }
                        else
                        {
                            oslcompiler.errorfmt(oslcompiler.filename(), oslcompiler.lineno(), "<unknown error>");
                        }
                    }
                    else if (pragmatype == "warning")
                    {
                        string msg;
                        if (ParseString(line, ref cursor, out msg))
                        {
                            oslcompiler.warningfmt(oslcompiler.filename(), oslcompiler.lineno(), msg);
                        }
                        else
                        {
                            oslcompiler.warningfmt(oslcompiler.filename(), oslcompiler.lineno(), "<unknown warning>");
                        }
                    }
                    else if (string.Equals(pragmatype, "osl", StringComparison.OrdinalIgnoreCase))
                    {
                        string pragmaname = ParseWord(line, ref cursor);
                        if (pragmaname == "nowarn")
                        {
                            oslcompiler.pragma_nowarn();
                        }
                        else
                        {
                            oslcompiler.warningfmt(oslcompiler.filename(), oslcompiler.lineno(), "Unknown pragma '" + pragmaname + "'");
                        }
                    }
                    // N.B. Any other pragmas that don't start with "osl" are ignored
                    oslcompiler.incr_lineno();  // the pragma ends with an EOLN
                }
                else
                {
                    // probably the line number and filename
                    if (string.CompareOrdinal(str, p, "line", 0, 4) == 0)
                    {
                        p += 4;
                    }
                    int line = Atoi(str, p);
                    if (line > 0)
                    {
                        int f = str.IndexOf('"');
                        if (f >= 0)
                        {
                            ++f;  // increment to past the quote
                            int len = 0;  // count of chars within quotes
                            while (f + len < str.Length && str[f + len] != '"')
                            {
                                ++len;
                            }
                            string filename = str.Substring(f, len);
                            // Make it relative to the working directory, so error output isn't hugely cluttered (and also to make the
                            // preprocessor's error output match the standard cpp.
                            string cwd = oslcompiler.cwd();
                            if (cwd.Length > 0 && filename.StartsWith(cwd, StringComparison.Ordinal))
                            {
                                filename = filename.Substring(cwd.Length);
                                if (filename.Length > 0 && (filename[0] == '/' || filename[0] == '\\'))
                                {
                                    filename = filename.Substring(1);
                                }
                            }
                            string ufilename = string.Intern(filename);
                            oslcompiler.filename(ufilename);
                            if (ufilename == oslcompiler.main_filename())
                            {
                                --line;
                            }
                        }
                        oslcompiler.lineno(line);
                    }
                    else
                    {
                        oslcompiler.errorfmt(oslcompiler.filename(), oslcompiler.lineno(),
                                             "Unrecognized preprocessor command: #" + str.Substring(p));
                    }
                }
                SETLINE(ref yylloc);
            }

            // C's atoi: leading white space, a sign, digits
            private static int Atoi(string s, int p)
            {
                while (p < s.Length && (s[p] == ' ' || s[p] == '\t'))
                {
                    ++p;
                }
                bool negative = false;
                if (p < s.Length && (s[p] == '-' || s[p] == '+'))
                {
                    negative = s[p] == '-';
                    ++p;
                }
                long value = 0;
                while (p < s.Length && char.IsAsciiDigit(s[p]))
                {
                    value = Math.Min(value * 10 + (s[p] - '0'), int.MaxValue);
                    ++p;
                }
                return (int)(negative ? -value : value);
            }

            // OIIO::Strutil::parse_word: skip white space, then letters, digits and underscores starting with a letter or underscore
            private static string ParseWord(string s, ref int cursor)
            {
                while (cursor < s.Length && char.IsWhiteSpace(s[cursor]))
                {
                    ++cursor;
                }
                int start = cursor;
                while (cursor < s.Length && (char.IsAsciiLetterOrDigit(s[cursor]) || s[cursor] == '_'))
                {
                    ++cursor;
                }
                return s.Substring(start, cursor - start);
            }

            // OIIO::Strutil::parse_string: skip white space, then a string in double quotes (the quotes are not part of the result)
            private static bool ParseString(string s, ref int cursor, out string result)
            {
                result = null;
                int c = cursor;
                while (c < s.Length && char.IsWhiteSpace(s[c]))
                {
                    ++c;
                }
                if (c >= s.Length || s[c] != '"')
                {
                    return false;
                }
                int start = ++c;
                while (c < s.Length && s[c] != '"')
                {
                    if (s[c] == '\\')
                    {
                        ++c;
                    }
                    ++c;
                }
                if (c >= s.Length)
                {
                    return false;
                }
                result = s.Substring(start, c - start);
                cursor = c + 1;
                return true;
            }

            // OslLexer Private Members
            private readonly string buffer;
            private readonly IOslLexerHost oslcompiler;
            private int position;
            private bool atLineStart;
        }

    }  // namespace pvt
}  // namespace OSL
