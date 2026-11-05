// OSL port helper: std::regex (ECMAScript grammar) to .NET Regex translation, used by ShadingContext.find_regex.
//
// OSL compiles its regular expressions with std::regex using the default ECMAScript grammar. The .NET dialect differs in ways that change
// matching results, so the pattern is rewritten before it is handed to System.Text.RegularExpressions:
//   * \d \D \w \W \s \S are ASCII only in ECMAScript ([0-9], [A-Za-z0-9_], [ \t\n\v\f\r]); .NET uses Unicode categories.
//   * '.' does not match \n or \r in ECMAScript; in .NET it excludes only \n.
//   * '$' (non-multiline) matches only at the very end of the subject; .NET also matches before a final '\n'. It is rewritten to \z.
//   * POSIX classes inside brackets, [[:alpha:]] and friends, are valid in std::regex and become explicit ranges.
//   * Inside a bracket expression the negated shorthands \D \W \S are left to .NET (Unicode semantics); this is a remaining difference.
//   * \A, \z and the group forms (?: ), (?= ), (?! ) pass through unchanged; \A and \z are not ECMAScript escapes (they would be identity
//     escapes), but the runtime itself uses them to implement regex_match, so they keep their .NET meaning.
//   * Features that .NET accepts but std::regex rejects (named groups, lookbehind, possessive/atomic constructs, inline options) are not
//     diagnosed; they simply work. Backtracking order is the same leftmost-first ECMAScript semantics in both engines.

using System.Text;

namespace OSL
{
    public static class EcmaRegex
    {
        private static string posix_class(string name)
        {
            switch (name)
            {
                case "alpha": return "A-Za-z";
                case "digit": return "0-9";
                case "alnum": return "A-Za-z0-9";
                case "upper": return "A-Z";
                case "lower": return "a-z";
                case "space": return " \\t\\n\\v\\f\\r";
                case "blank": return " \\t";
                case "punct": return "!-/:-@\\[-`{-~";
                case "print": return " -~";
                case "graph": return "!-~";
                case "cntrl": return "\\x00-\\x1f\\x7f";
                case "xdigit": return "0-9A-Fa-f";
                case "w": return "A-Za-z0-9_";
                default: return null;
            }
        }

        public static string translate(string pattern)
        {
            StringBuilder sb = new StringBuilder(pattern.Length + 16);
            bool in_class = false;
            int i = 0;
            while (i < pattern.Length)
            {
                char c = pattern[i];
                if (c == '\\' && i + 1 < pattern.Length)
                {
                    char n = pattern[i + 1];
                    i += 2;
                    switch (n)
                    {
                        case 'd':
                            sb.Append(in_class ? "0-9" : "[0-9]");
                            break;
                        case 'D':
                            sb.Append(in_class ? "\\D" : "[^0-9]");
                            break;
                        case 'w':
                            sb.Append(in_class ? "A-Za-z0-9_" : "[A-Za-z0-9_]");
                            break;
                        case 'W':
                            sb.Append(in_class ? "\\W" : "[^A-Za-z0-9_]");
                            break;
                        case 's':
                            sb.Append(in_class ? " \\t\\n\\v\\f\\r" : "[ \\t\\n\\v\\f\\r]");
                            break;
                        case 'S':
                            sb.Append(in_class ? "\\S" : "[^ \\t\\n\\v\\f\\r]");
                            break;
                        default:
                            sb.Append('\\');
                            sb.Append(n);
                            break;
                    }
                    continue;
                }
                if (in_class)
                {
                    if (c == '[' && i + 1 < pattern.Length && pattern[i + 1] == ':')
                    {
                        int end = pattern.IndexOf(":]", i + 2, System.StringComparison.Ordinal);
                        if (end > 0)
                        {
                            string expansion = posix_class(pattern.Substring(i + 2, end - (i + 2)));
                            if (expansion != null)
                            {
                                sb.Append(expansion);
                                i = end + 2;
                                continue;
                            }
                        }
                    }
                    if (c == '[')
                    {
                        // A literal '[' inside a bracket expression; .NET treats it as a subtraction start only after '-', so escape it.
                        sb.Append("\\[");
                        ++i;
                        continue;
                    }
                    if (c == ']')
                    {
                        in_class = false;
                    }
                    sb.Append(c);
                    ++i;
                    continue;
                }
                if (c == '[')
                {
                    in_class = true;
                    sb.Append('[');
                    ++i;
                    if (i < pattern.Length && pattern[i] == '^')
                    {
                        sb.Append('^');
                        ++i;
                    }
                    // A ']' first in the set is a literal in ECMAScript-with-POSIX grammars as in .NET.
                    if (i < pattern.Length && pattern[i] == ']')
                    {
                        sb.Append("\\]");
                        ++i;
                    }
                    continue;
                }
                if (c == '.')
                {
                    sb.Append("[^\\n\\r]");
                    ++i;
                    continue;
                }
                if (c == '$')
                {
                    sb.Append("\\z");
                    ++i;
                    continue;
                }
                sb.Append(c);
                ++i;
            }
            return sb.ToString();
        }
    }
}
