// OSL port: the C preprocessor
//
// OSLCompilerImpl::preprocess_buffer runs the clang preprocessor over the shader source, with "-E"-style output that carries line markers
// ('# 12 "file"'). There is no clang here, so this file is a C preprocessor with the same observable behavior for what shaders use:
// #include, #define / #undef (object-like and function-like macros, # and ## operators, __VA_ARGS__), #if / #ifdef / #ifndef / #elif /
// #else / #endif with the usual constant expressions, #error, #warning, #pragma (passed on to the scanner), #line, __FILE__ and __LINE__.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OIIO;

namespace OSL
{
    namespace pvt
    {
        public sealed class CPreprocessor
        {
            private enum TokKind { Ident, Number, String, Char, Punct }

            private struct Tok
            {
                public TokKind kind;
                public string text;
                public bool space;  // preceded by white space
                public Tok(TokKind kind, string text, bool space)
                {
                    this.kind = kind;
                    this.text = text;
                    this.space = space;
                }
            }

            private sealed class Macro
            {
                public bool isfunc;
                public bool variadic;
                public List<string> parms = new List<string>();
                public List<Tok> body = new List<Tok>();
            }

            private sealed class Cond
            {
                public bool parentActive;  // enclosing conditions are all true
                public bool taken;         // some branch of this #if chain has been taken
                public bool active;        // the current branch is being emitted
                public bool seenElse;
            }

            private Dictionary<string, Macro> macros = new Dictionary<string, Macro>();
            private List<string> includepaths;
            private StringBuilder output = new StringBuilder();
            private List<string> errors = new List<string>();
            private string curfile;
            private int curline;

            public CPreprocessor(List<string> includepaths)
            {
                this.includepaths = includepaths;
            }

            public List<string> Errors { get { return errors; } }

            public void Define(string definition)
            {
                // "NAME", "NAME=value" or "NAME(args)=body"; a bare NAME is defined as 1
                int eq = definition.IndexOf('=');
                string head = eq >= 0 ? definition.Substring(0, eq) : definition;
                string value = eq >= 0 ? definition.Substring(eq + 1) : "1";
                DefineDirective(head + " " + value);
            }

            public void Undefine(string name)
            {
                macros.Remove(name);
            }

            public string Run(string text, string filename)
            {
                output.Clear();
                output.Append("# 1 \"" + filename + "\"\n");
                ProcessFile(text, filename, 0);
                return output.ToString();
            }

            // A logical line: backslash-newlines spliced and comments replaced by a space
            private sealed class Line
            {
                public string text;
                public int startLine;
                public int endLine;  // the physical line after the last one it covers
            }

            private static List<Line> SplitLines(string src)
            {
                List<Line> lines = new List<Line>();
                StringBuilder sb = new StringBuilder();
                int physical = 1;
                int start = 1;
                int i = 0;
                int n = src.Length;
                bool inBlock = false;
                while (i < n)
                {
                    char c = src[i];
                    if (c == '\r')
                    {
                        ++i;
                        continue;
                    }
                    if (inBlock)
                    {
                        if (c == '*' && i + 1 < n && src[i + 1] == '/')
                        {
                            inBlock = false;
                            i += 2;
                            sb.Append(' ');
                        }
                        else
                        {
                            if (c == '\n')
                            {
                                ++physical;
                            }
                            ++i;
                        }
                        continue;
                    }
                    if (c == '\\' && i + 1 < n && (src[i + 1] == '\n' || (src[i + 1] == '\r' && i + 2 < n && src[i + 2] == '\n')))
                    {
                        i += src[i + 1] == '\r' ? 3 : 2;
                        ++physical;
                        continue;
                    }
                    if (c == '/' && i + 1 < n && src[i + 1] == '*')
                    {
                        inBlock = true;
                        i += 2;
                        continue;
                    }
                    if (c == '/' && i + 1 < n && src[i + 1] == '/')
                    {
                        while (i < n && src[i] != '\n')
                        {
                            if (src[i] == '\\' && i + 1 < n && src[i + 1] == '\n')
                            {
                                ++physical;
                                i += 2;
                                continue;
                            }
                            ++i;
                        }
                        continue;
                    }
                    if (c == '"' || c == '\'')
                    {
                        char q = c;
                        sb.Append(c);
                        ++i;
                        while (i < n && src[i] != q && src[i] != '\n')
                        {
                            if (src[i] == '\\' && i + 1 < n && (src[i + 1] == '\n' || (src[i + 1] == '\r' && i + 2 < n && src[i + 2] == '\n')))
                            {
                                i += src[i + 1] == '\r' ? 3 : 2;  // a line splice inside the string
                                ++physical;
                                continue;
                            }
                            if (src[i] == '\\' && i + 1 < n && src[i + 1] != '\n')
                            {
                                sb.Append(src[i]);
                                ++i;
                            }
                            sb.Append(src[i]);
                            ++i;
                        }
                        if (i < n && src[i] == q)
                        {
                            sb.Append(q);
                            ++i;
                        }
                        continue;
                    }
                    if (c == '\n')
                    {
                        ++physical;
                        lines.Add(new Line { text = sb.ToString(), startLine = start, endLine = physical });
                        sb.Clear();
                        start = physical;
                        ++i;
                        continue;
                    }
                    sb.Append(c);
                    ++i;
                }
                if (sb.Length > 0 || start < physical)
                {
                    lines.Add(new Line { text = sb.ToString(), startLine = start, endLine = physical + 1 });
                }
                return lines;
            }

            private static bool IsIdStart(char c)
            {
                return char.IsLetter(c) || c == '_';
            }

            private static bool IsIdChar(char c)
            {
                return char.IsLetterOrDigit(c) || c == '_';
            }

            private static readonly string[] punct3 = { "<<=", ">>=", "..." };
            private static readonly string[] punct2 = { "##", "++", "--", "&&", "||", "==", "!=", "<=", ">=", "<<", ">>", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "->" };

            private static List<Tok> Tokenize(string s)
            {
                List<Tok> toks = new List<Tok>();
                int i = 0;
                bool space = false;
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c == ' ' || c == '\t' || c == '\f' || c == '\v')
                    {
                        space = true;
                        ++i;
                        continue;
                    }
                    int start = i;
                    if (IsIdStart(c))
                    {
                        while (i < s.Length && IsIdChar(s[i]))
                        {
                            ++i;
                        }
                        toks.Add(new Tok(TokKind.Ident, s.Substring(start, i - start), space));
                    }
                    else if (char.IsDigit(c) || (c == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
                    {
                        ++i;
                        while (i < s.Length)
                        {
                            char d = s[i];
                            if ((d == '+' || d == '-') && (s[i - 1] == 'e' || s[i - 1] == 'E'))
                            {
                                ++i;
                            }
                            else if (IsIdChar(d) || d == '.')
                            {
                                ++i;
                            }
                            else
                            {
                                break;
                            }
                        }
                        toks.Add(new Tok(TokKind.Number, s.Substring(start, i - start), space));
                    }
                    else if (c == '"' || c == '\'')
                    {
                        ++i;
                        while (i < s.Length && s[i] != c)
                        {
                            if (s[i] == '\\' && i + 1 < s.Length)
                            {
                                ++i;
                            }
                            ++i;
                        }
                        if (i < s.Length)
                        {
                            ++i;
                        }
                        toks.Add(new Tok(c == '"' ? TokKind.String : TokKind.Char, s.Substring(start, i - start), space));
                    }
                    else
                    {
                        string p = null;
                        foreach (string q in punct3)
                        {
                            if (string.CompareOrdinal(s, i, q, 0, 3) == 0)
                            {
                                p = q;
                                break;
                            }
                        }
                        if (p == null)
                        {
                            foreach (string q in punct2)
                            {
                                if (string.CompareOrdinal(s, i, q, 0, 2) == 0)
                                {
                                    p = q;
                                    break;
                                }
                            }
                        }
                        if (p == null)
                        {
                            p = c.ToString();
                        }
                        i += p.Length;
                        toks.Add(new Tok(TokKind.Punct, p, space));
                    }
                    space = false;
                }
                return toks;
            }

            private void Error(string msg)
            {
                errors.Add(curfile + ":" + curline + ": " + msg);
            }

            private void ProcessFile(string text, string filename, int depth)
            {
                if (depth > 200)
                {
                    Error("#include nested too deeply");
                    return;
                }
                string savedFile = curfile;
                int savedLine = curline;
                curfile = filename;
                List<Line> lines = SplitLines(text);
                List<Cond> conds = new List<Cond>();
                int outLine = 1;  // the line number the output stream is at
                bool active = true;

                for (int li = 0; li < lines.Count; ++li)
                {
                    Line line = lines[li];
                    curline = line.startLine;
                    string trimmed = line.text.TrimStart(' ', '\t');
                    if (trimmed.StartsWith("#", StringComparison.Ordinal))
                    {
                        string dir = trimmed.Substring(1).TrimStart(' ', '\t');
                        int e = 0;
                        while (e < dir.Length && IsIdChar(dir[e]))
                        {
                            ++e;
                        }
                        string name = dir.Substring(0, e);
                        string rest = dir.Substring(e).Trim(' ', '\t');
                        bool parentActive = conds.Count == 0 || conds[conds.Count - 1].active;
                        switch (name)
                        {
                            case "if":
                            case "ifdef":
                            case "ifndef":
                                {
                                    Cond c = new Cond();
                                    c.parentActive = parentActive;
                                    bool v = false;
                                    if (parentActive)
                                    {
                                        if (name == "if")
                                        {
                                            v = EvalCondition(rest);
                                        }
                                        else
                                        {
                                            bool defd = macros.ContainsKey(FirstWord(rest));
                                            v = name == "ifdef" ? defd : !defd;
                                        }
                                    }
                                    c.taken = v;
                                    c.active = parentActive && v;
                                    conds.Add(c);
                                }
                                break;
                            case "elif":
                                if (conds.Count == 0)
                                {
                                    Error("#elif without #if");
                                }
                                else
                                {
                                    Cond c = conds[conds.Count - 1];
                                    if (c.parentActive && !c.taken && EvalCondition(rest))
                                    {
                                        c.taken = true;
                                        c.active = true;
                                    }
                                    else
                                    {
                                        c.active = false;
                                    }
                                }
                                break;
                            case "else":
                                if (conds.Count == 0)
                                {
                                    Error("#else without #if");
                                }
                                else
                                {
                                    Cond c = conds[conds.Count - 1];
                                    c.active = c.parentActive && !c.taken;
                                    c.taken = true;
                                    c.seenElse = true;
                                }
                                break;
                            case "endif":
                                if (conds.Count == 0)
                                {
                                    Error("#endif without #if");
                                }
                                else
                                {
                                    conds.RemoveAt(conds.Count - 1);
                                }
                                break;
                            default:
                                if (parentActive)
                                {
                                    switch (name)
                                    {
                                        case "define":
                                            DefineDirective(rest);
                                            break;
                                        case "undef":
                                            macros.Remove(FirstWord(rest));
                                            break;
                                        case "include":
                                            {
                                                string path = ResolveInclude(rest, filename);
                                                if (path == null)
                                                {
                                                    Error("'" + rest.Trim('"', '<', '>') + "' file not found");
                                                }
                                                else
                                                {
                                                    string incText;
                                                    try
                                                    {
                                                        incText = File.ReadAllText(path);
                                                    }
                                                    catch (Exception)
                                                    {
                                                        Error("Could not read '" + path + "'");
                                                        break;
                                                    }
                                                    output.Append("# 1 \"" + path + "\" 1\n");
                                                    ProcessFile(incText, path, depth + 1);
                                                    curfile = filename;
                                                    outLine = line.endLine;
                                                    output.Append("# " + outLine + " \"" + filename + "\" 2\n");
                                                }
                                            }
                                            break;
                                        case "error":
                                            Error("#error " + rest);
                                            break;
                                        case "warning":
                                            break;
                                        case "pragma":
                                            // handed to the scanner, which acts on error / warning / osl pragmas
                                            if (line.startLine > outLine)
                                            {
                                                SyncLine(ref outLine, line.startLine, filename);
                                            }
                                            output.Append("#pragma " + rest + "\n");
                                            outLine = line.startLine + 1;
                                            break;
                                        case "line":
                                            break;
                                        case "":
                                            break;
                                        default:
                                            Error("invalid preprocessing directive #" + name);
                                            break;
                                    }
                                }
                                break;
                        }
                        active = conds.Count == 0 || conds[conds.Count - 1].active;
                        continue;
                    }
                    if (!active)
                    {
                        continue;
                    }

                    // An ordinary text line. A function-like macro invocation may run on over several lines.
                    string logical = line.text;
                    int endLine = line.endLine;
                    int lastStart = line.startLine;
                    while (UnbalancedMacroCall(logical) && li + 1 < lines.Count && !lines[li + 1].text.TrimStart(' ', '\t').StartsWith("#", StringComparison.Ordinal))
                    {
                        ++li;
                        logical += " " + lines[li].text;
                        endLine = lines[li].endLine;
                        lastStart = lines[li].startLine;
                    }
                    // clang's __LINE__ is the line of the closing parenthesis of the outermost macro invocation
                    curline = lastStart;
                    if (logical.Trim().Length == 0)
                    {
                        continue;
                    }
                    List<Tok> expanded = Expand(Tokenize(logical), new HashSet<string>());
                    SyncLine(ref outLine, line.startLine, filename);
                    output.Append(Join(expanded));
                    output.Append('\n');
                    outLine = line.startLine + 1;
                    // a statement that took several physical lines leaves the output behind; the next line catches up with a marker or blanks
                }

                if (conds.Count != 0)
                {
                    Error("unterminated conditional directive");
                }
                curfile = savedFile;
                curline = savedLine;
            }

            private void SyncLine(ref int outLine, int target, string filename)
            {
                if (target == outLine)
                {
                    return;
                }
                if (target > outLine && target - outLine <= 8)
                {
                    for (int k = outLine; k < target; ++k)
                    {
                        output.Append('\n');
                    }
                }
                else
                {
                    output.Append("# " + target + " \"" + filename + "\"\n");
                }
                outLine = target;
            }

            private static string FirstWord(string s)
            {
                int e = 0;
                while (e < s.Length && IsIdChar(s[e]))
                {
                    ++e;
                }
                return s.Substring(0, e);
            }

            private string ResolveInclude(string spec, string fromFile)
            {
                spec = spec.Trim();
                if (spec.Length > 0 && !(spec[0] == '"' || spec[0] == '<'))
                {
                    // a macro that expands to the file name
                    List<Tok> t = Expand(Tokenize(spec), new HashSet<string>());
                    spec = Join(t).Trim();
                }
                if (spec.Length < 2)
                {
                    return null;
                }
                bool quoted = spec[0] == '"';
                int close = spec.IndexOf(quoted ? '"' : '>', 1);
                if (close < 0)
                {
                    return null;
                }
                string name = spec.Substring(1, close - 1);
                if (Path.IsPathRooted(name))
                {
                    return File.Exists(name) ? name : null;
                }
                if (quoted)
                {
                    string dir = Path.GetDirectoryName(fromFile);
                    string cand = string.IsNullOrEmpty(dir) ? name : Path.Combine(dir, name);
                    if (File.Exists(cand))
                    {
                        return cand;
                    }
                }
                foreach (string dir in includepaths)
                {
                    string cand = Path.Combine(dir, name);
                    if (File.Exists(cand))
                    {
                        return cand;
                    }
                }
                return null;
            }

            private void DefineDirective(string rest)
            {
                int e = 0;
                while (e < rest.Length && IsIdChar(rest[e]))
                {
                    ++e;
                }
                string name = rest.Substring(0, e);
                if (name.Length == 0)
                {
                    Error("macro name missing");
                    return;
                }
                Macro m = new Macro();
                string body;
                if (e < rest.Length && rest[e] == '(')
                {
                    m.isfunc = true;
                    int close = rest.IndexOf(')', e);
                    if (close < 0)
                    {
                        Error("missing ')' in macro parameter list");
                        return;
                    }
                    string plist = rest.Substring(e + 1, close - e - 1);
                    foreach (string p in plist.Split(','))
                    {
                        string pp = p.Trim();
                        if (pp.Length == 0)
                        {
                            continue;
                        }
                        if (pp == "...")
                        {
                            m.variadic = true;
                            m.parms.Add("__VA_ARGS__");
                        }
                        else if (pp.EndsWith("...", StringComparison.Ordinal))
                        {
                            m.variadic = true;
                            m.parms.Add(pp.Substring(0, pp.Length - 3));
                        }
                        else
                        {
                            m.parms.Add(pp);
                        }
                    }
                    body = rest.Substring(close + 1);
                }
                else
                {
                    body = rest.Substring(e);
                }
                m.body = Tokenize(body);
                if (m.body.Count > 0)
                {
                    Tok first = m.body[0];
                    first.space = false;
                    m.body[0] = first;
                }
                macros[name] = m;
            }

            private bool UnbalancedMacroCall(string text)
            {
                List<Tok> toks = Tokenize(text);
                for (int i = 0; i < toks.Count; ++i)
                {
                    Macro m;
                    if (toks[i].kind == TokKind.Ident && macros.TryGetValue(toks[i].text, out m) && m.isfunc)
                    {
                        if (i + 1 < toks.Count && toks[i + 1].text == "(")
                        {
                            int depth = 0;
                            int j = i + 1;
                            for (; j < toks.Count; ++j)
                            {
                                if (toks[j].text == "(")
                                {
                                    ++depth;
                                }
                                else if (toks[j].text == ")")
                                {
                                    if (--depth == 0)
                                    {
                                        break;
                                    }
                                }
                            }
                            if (j >= toks.Count)
                            {
                                return true;
                            }
                            i = j;
                        }
                    }
                }
                return false;
            }

            private static string Join(List<Tok> toks)
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < toks.Count; ++i)
                {
                    Tok t = toks[i];
                    bool space = t.space && i > 0;
                    if (!space && sb.Length > 0 && t.text.Length > 0)
                    {
                        char a = sb[sb.Length - 1];
                        char b = t.text[0];
                        if ((IsIdChar(a) && IsIdChar(b)) || (a == b && "+-&|<>=#".IndexOf(a) >= 0) || (a == '/' && b == '*') || (a == '.' && char.IsDigit(b)))
                        {
                            space = true;
                        }
                    }
                    if (space)
                    {
                        sb.Append(' ');
                    }
                    sb.Append(t.text);
                }
                return sb.ToString();
            }

            private List<Tok> Expand(List<Tok> input, HashSet<string> disabled)
            {
                List<Tok> output = new List<Tok>();
                int i = 0;
                while (i < input.Count)
                {
                    Tok t = input[i];
                    Macro m;
                    if (t.kind != TokKind.Ident)
                    {
                        output.Add(t);
                        ++i;
                        continue;
                    }
                    if (t.text == "__LINE__")
                    {
                        output.Add(new Tok(TokKind.Number, curline.ToString(), t.space));
                        ++i;
                        continue;
                    }
                    if (t.text == "__FILE__")
                    {
                        output.Add(new Tok(TokKind.String, "\"" + curfile + "\"", t.space));
                        ++i;
                        continue;
                    }
                    if (disabled.Contains(t.text) || !macros.TryGetValue(t.text, out m))
                    {
                        output.Add(t);
                        ++i;
                        continue;
                    }
                    List<Tok> replacement;
                    int consumed;
                    if (!m.isfunc)
                    {
                        replacement = new List<Tok>(m.body);
                        consumed = 1;
                    }
                    else
                    {
                        // Needs an argument list
                        int j = i + 1;
                        if (j >= input.Count || input[j].text != "(")
                        {
                            output.Add(t);
                            ++i;
                            continue;
                        }
                        List<List<Tok>> args = new List<List<Tok>>();
                        List<Tok> cur = new List<Tok>();
                        int depth = 0;
                        ++j;
                        bool closed = false;
                        for (; j < input.Count; ++j)
                        {
                            Tok a = input[j];
                            if (a.text == "(")
                            {
                                ++depth;
                            }
                            else if (a.text == ")")
                            {
                                if (depth == 0)
                                {
                                    closed = true;
                                    break;
                                }
                                --depth;
                            }
                            else if (a.text == "," && depth == 0 && !(m.variadic && args.Count >= m.parms.Count - 1))
                            {
                                args.Add(cur);
                                cur = new List<Tok>();
                                continue;
                            }
                            cur.Add(a);
                        }
                        if (!closed)
                        {
                            Error("unterminated argument list invoking macro \"" + t.text + "\"");
                            output.Add(t);
                            ++i;
                            continue;
                        }
                        args.Add(cur);
                        if (m.parms.Count == 0 && args.Count == 1 && args[0].Count == 0)
                        {
                            args.Clear();
                        }
                        if (args.Count < m.parms.Count && m.variadic && args.Count == m.parms.Count - 1)
                        {
                            args.Add(new List<Tok>());
                        }
                        if (args.Count != m.parms.Count)
                        {
                            Error("macro \"" + t.text + "\" requires " + m.parms.Count + " arguments, but " + args.Count + " given");
                            output.Add(t);
                            i = j + 1;
                            continue;
                        }
                        replacement = Substitute(m, args, disabled);
                        consumed = j + 1 - i;
                    }
                    HashSet<string> inner = new HashSet<string>(disabled);
                    inner.Add(t.text);
                    List<Tok> rescanned = Expand(replacement, inner);
                    if (rescanned.Count > 0)
                    {
                        Tok first = rescanned[0];
                        first.space = t.space;
                        rescanned[0] = first;
                    }
                    output.AddRange(rescanned);
                    i += consumed;
                }
                return output;
            }

            private List<Tok> Substitute(Macro m, List<List<Tok>> args, HashSet<string> disabled)
            {
                List<Tok> result = new List<Tok>();
                List<Tok> body = m.body;
                for (int i = 0; i < body.Count; ++i)
                {
                    Tok t = body[i];
                    // The # operator: stringify an argument
                    if (t.text == "#" && t.kind == TokKind.Punct && i + 1 < body.Count && m.parms.Contains(body[i + 1].text))
                    {
                        List<Tok> arg = args[m.parms.IndexOf(body[i + 1].text)];
                        string s = Join(arg).Replace("\\", "\\\\").Replace("\"", "\\\"");
                        result.Add(new Tok(TokKind.String, "\"" + s + "\"", t.space));
                        ++i;
                        continue;
                    }
                    bool pasteNext = i + 1 < body.Count && body[i + 1].text == "##" && body[i + 1].kind == TokKind.Punct;
                    bool pastePrev = result.Count > 0 && pendingPaste;
                    if (t.text == "##" && t.kind == TokKind.Punct)
                    {
                        continue;  // handled with its left operand
                    }
                    List<Tok> operand;
                    int pi = t.kind == TokKind.Ident ? m.parms.IndexOf(t.text) : -1;
                    if (pi >= 0)
                    {
                        // Arguments next to ## are used as they are; the others are expanded first
                        operand = (pasteNext || pastePrev) ? new List<Tok>(args[pi]) : Expand(args[pi], disabled);
                        if (operand.Count > 0)
                        {
                            Tok f = operand[0];
                            f.space = t.space;
                            operand[0] = f;
                        }
                    }
                    else
                    {
                        operand = new List<Tok> { t };
                    }
                    if (pastePrev)
                    {
                        // glue the first token of this operand to the last token already produced
                        if (operand.Count > 0 && result.Count > 0)
                        {
                            Tok left = result[result.Count - 1];
                            string glued = left.text + operand[0].text;
                            List<Tok> g = Tokenize(glued);
                            result.RemoveAt(result.Count - 1);
                            for (int k = 0; k < g.Count; ++k)
                            {
                                Tok gt = g[k];
                                if (k == 0)
                                {
                                    gt.space = left.space;
                                }
                                result.Add(gt);
                            }
                            operand.RemoveAt(0);
                        }
                        pendingPaste = false;
                    }
                    result.AddRange(operand);
                    if (pasteNext)
                    {
                        pendingPaste = true;
                    }
                }
                pendingPaste = false;
                return result;
            }

            private bool pendingPaste;

            // #if expressions
            private bool EvalCondition(string expr)
            {
                // replace defined(X) / defined X before macro expansion
                List<Tok> toks = Tokenize(expr);
                List<Tok> prepared = new List<Tok>();
                for (int i = 0; i < toks.Count; ++i)
                {
                    if (toks[i].kind == TokKind.Ident && toks[i].text == "defined")
                    {
                        int j = i + 1;
                        bool paren = j < toks.Count && toks[j].text == "(";
                        if (paren)
                        {
                            ++j;
                        }
                        string name = j < toks.Count ? toks[j].text : "";
                        if (paren)
                        {
                            ++j;
                        }
                        prepared.Add(new Tok(TokKind.Number, macros.ContainsKey(name) ? "1" : "0", true));
                        i = j;
                        continue;
                    }
                    prepared.Add(toks[i]);
                }
                List<Tok> expanded = Expand(prepared, new HashSet<string>());
                exprToks = expanded;
                exprPos = 0;
                long v = ParseTernary();
                return v != 0;
            }

            private List<Tok> exprToks;
            private int exprPos;

            private string Peek()
            {
                return exprPos < exprToks.Count ? exprToks[exprPos].text : "";
            }

            private long ParseTernary()
            {
                long c = ParseBinary(0);
                if (Peek() == "?")
                {
                    ++exprPos;
                    long a = ParseTernary();
                    if (Peek() == ":")
                    {
                        ++exprPos;
                    }
                    long b = ParseTernary();
                    return c != 0 ? a : b;
                }
                return c;
            }

            private static readonly string[][] binaryLevels =
            {
                new[] { "||" }, new[] { "&&" }, new[] { "|" }, new[] { "^" }, new[] { "&" }, new[] { "==", "!=" },
                new[] { "<", ">", "<=", ">=" }, new[] { "<<", ">>" }, new[] { "+", "-" }, new[] { "*", "/", "%" }
            };

            private long ParseBinary(int level)
            {
                if (level >= binaryLevels.Length)
                {
                    return ParseUnary();
                }
                long left = ParseBinary(level + 1);
                while (Array.IndexOf(binaryLevels[level], Peek()) >= 0)
                {
                    string op = Peek();
                    ++exprPos;
                    long right = ParseBinary(level + 1);
                    switch (op)
                    {
                        case "||": left = (left != 0 || right != 0) ? 1 : 0; break;
                        case "&&": left = (left != 0 && right != 0) ? 1 : 0; break;
                        case "|": left |= right; break;
                        case "^": left ^= right; break;
                        case "&": left &= right; break;
                        case "==": left = left == right ? 1 : 0; break;
                        case "!=": left = left != right ? 1 : 0; break;
                        case "<": left = left < right ? 1 : 0; break;
                        case ">": left = left > right ? 1 : 0; break;
                        case "<=": left = left <= right ? 1 : 0; break;
                        case ">=": left = left >= right ? 1 : 0; break;
                        case "<<": left <<= (int)right; break;
                        case ">>": left >>= (int)right; break;
                        case "+": left += right; break;
                        case "-": left -= right; break;
                        case "*": left *= right; break;
                        case "/": left = right != 0 ? left / right : 0; break;
                        case "%": left = right != 0 ? left % right : 0; break;
                    }
                }
                return left;
            }

            private long ParseUnary()
            {
                string p = Peek();
                if (p == "!")
                {
                    ++exprPos;
                    return ParseUnary() == 0 ? 1 : 0;
                }
                if (p == "~")
                {
                    ++exprPos;
                    return ~ParseUnary();
                }
                if (p == "-")
                {
                    ++exprPos;
                    return -ParseUnary();
                }
                if (p == "+")
                {
                    ++exprPos;
                    return ParseUnary();
                }
                if (p == "(")
                {
                    ++exprPos;
                    long v = ParseTernary();
                    if (Peek() == ")")
                    {
                        ++exprPos;
                    }
                    return v;
                }
                if (exprPos < exprToks.Count)
                {
                    Tok t = exprToks[exprPos++];
                    if (t.kind == TokKind.Number)
                    {
                        string s = t.text.TrimEnd('u', 'U', 'l', 'L');
                        long v;
                        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && long.TryParse(s.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out v))
                        {
                            return v;
                        }
                        if (long.TryParse(s, out v))
                        {
                            return v;
                        }
                        return 0;
                    }
                    if (t.kind == TokKind.Char && t.text.Length >= 3)
                    {
                        return t.text[1];
                    }
                }
                return 0;  // an identifier that is not a macro
            }
        }

        public partial class OSLCompilerImpl
        {
            public bool preprocess_buffer(string buffer, string filename, string stdoslpath, List<string> defines, List<string> includepaths,
                                          out string result)
            {
                result = "";
                string instring;
                if (!string.IsNullOrEmpty(stdoslpath))
                {
                    // The path is placed inside a double-quoted string, so backslashes in Windows paths are escaped
                    instring = "#include \"" + Strutil.escape_chars(stdoslpath) + "\"\n";
                }
                else
                {
                    instring = "\n";
                }
                instring += buffer;

                string directory = Path.GetDirectoryName(filename);
                if (string.IsNullOrEmpty(directory))
                {
                    directory = Directory.GetCurrentDirectory();
                }
                List<string> paths = new List<string>();
                paths.Add(directory);
                paths.AddRange(includepaths);

                CPreprocessor pp = new CPreprocessor(paths);
                pp.Define("OSL_VERSION_MAJOR=" + OSL_LIBRARY_VERSION_MAJOR);
                pp.Define("OSL_VERSION_MINOR=" + OSL_LIBRARY_VERSION_MINOR);
                pp.Define("OSL_VERSION_PATCH=" + OSL_LIBRARY_VERSION_PATCH);
                pp.Define("OSL_VERSION=" + OSL_LIBRARY_VERSION_CODE);
                foreach (string d in defines)
                {
                    if (d[1] == 'D')
                    {
                        pp.Define(d.Substring(2));
                    }
                    else if (d[1] == 'U')
                    {
                        pp.Undefine(d.Substring(2));
                    }
                }

                result = pp.Run(instring, filename);
                if (pp.Errors.Count > 0)
                {
                    errorfmt(new ustring(), -1, string.Join("\n", pp.Errors));
                    return false;
                }
                return true;
            }

            public const int OSL_LIBRARY_VERSION_MAJOR = 1;
            public const int OSL_LIBRARY_VERSION_MINOR = 16;
            public const int OSL_LIBRARY_VERSION_PATCH = 0;
            public const int OSL_LIBRARY_VERSION_CODE = 10000 * OSL_LIBRARY_VERSION_MAJOR + 100 * OSL_LIBRARY_VERSION_MINOR + OSL_LIBRARY_VERSION_PATCH;
        }
    }
}
