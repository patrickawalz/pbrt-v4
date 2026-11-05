// OSL port: OpenImageIO ustring.h
//
// A ustring is an interned string: equal strings are the same object, so equality is a reference comparison and the hash is computed once.
// Interning goes through a process-wide table; the 64-bit hash is the one that ustringhash (the 'h' type code) carries.

using System;
using System.Collections.Concurrent;

namespace OIIO
{
    public struct ustring : IEquatable<ustring>, IComparable<ustring>
    {
        private sealed class Entry
        {
            public string text;
            public ulong hash;
        }

        private static readonly ConcurrentDictionary<string, Entry> table = new ConcurrentDictionary<string, Entry>(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<ulong, Entry> byHash = new ConcurrentDictionary<ulong, Entry>();
        private static readonly Entry emptyEntry = Make("");

        private Entry e;

        private static ulong Fnv64(string s)
        {
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < s.Length; ++i)
            {
                h ^= s[i];
                h *= 1099511628211UL;
            }
            return h;
        }

        private static Entry Make(string s)
        {
            return table.GetOrAdd(s, k =>
            {
                Entry n = new Entry();
                n.text = k;
                n.hash = Fnv64(k);
                byHash[n.hash] = n;
                return n;
            });
        }

        public ustring(string s)
        {
            e = s == null ? emptyEntry : Make(s);
        }

        public static ustring format(string s) { return new ustring(s); }

        public string @string() { return e == null ? "" : e.text; }
        public string c_str() { return @string(); }
        public int size() { return @string().Length; }
        public int length() { return size(); }
        public bool empty() { return size() == 0; }
        public ulong hash() { return e == null ? emptyEntry.hash : e.hash; }
        public char this[int i] { get { return @string()[i]; } }

        // The ustring that has the given hash, if one has been made (the reverse of hash())
        public static bool from_hash(ulong h, out ustring u)
        {
            Entry n;
            if (byHash.TryGetValue(h, out n))
            {
                u = new ustring();
                u.e = n;
                return true;
            }
            u = new ustring();
            return false;
        }

        public static bool operator ==(ustring a, ustring b) { return a.hash() == b.hash() && a.@string() == b.@string(); }
        public static bool operator !=(ustring a, ustring b) { return !(a == b); }
        public static bool operator ==(ustring a, string b) { return a.@string() == b; }
        public static bool operator !=(ustring a, string b) { return a.@string() != b; }
        public bool Equals(ustring o) { return this == o; }
        public override bool Equals(object o) { return o is ustring u && this == u; }
        public override int GetHashCode() { return (int)hash(); }
        public int CompareTo(ustring o) { return string.CompareOrdinal(@string(), o.@string()); }
        public override string ToString() { return @string(); }
        public static implicit operator string(ustring u) { return u.@string(); }
        public static implicit operator ustring(string s) { return new ustring(s); }
    }
}

namespace OIIO
{
    public static class Strutil
    {
        // The C printf "%.9g" rendering of a float (the shortest form that round-trips), as the .oso writer and the default-literal code use
        public static string format_g9(float f)
        {
            string r = f.ToString("G9", System.Globalization.CultureInfo.InvariantCulture);
            return r.Replace('E', 'e');
        }

        // Convert unprintable characters to C escape sequences (\n, \t, \", \\ ...), as OIIO::Strutil::escape_chars
        public static string escape_chars(string s)
        {
            System.Text.StringBuilder o = new System.Text.StringBuilder();
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\n': o.Append("\\n"); break;
                    case '\t': o.Append("\\t"); break;
                    case '\v': o.Append("\\v"); break;
                    case '\b': o.Append("\\b"); break;
                    case '\r': o.Append("\\r"); break;
                    case '\f': o.Append("\\f"); break;
                    case '\a': o.Append("\\a"); break;
                    case '\\': o.Append("\\\\"); break;
                    case '"': o.Append("\\\""); break;
                    default: o.Append(c); break;
                }
            }
            return o.ToString();
        }

        // The reverse: C escape sequences, including octal \ooo and hex \xhh, back to characters
        public static string unescape_chars(string s)
        {
            System.Text.StringBuilder o = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; ++i)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length)
                {
                    o.Append(c);
                    continue;
                }
                c = s[++i];
                switch (c)
                {
                    case 'n': o.Append('\n'); break;
                    case 't': o.Append('\t'); break;
                    case 'v': o.Append('\v'); break;
                    case 'b': o.Append('\b'); break;
                    case 'r': o.Append('\r'); break;
                    case 'f': o.Append('\f'); break;
                    case 'a': o.Append('\a'); break;
                    case 'x':
                        {
                            int v = 0, n = 0;
                            while (n < 2 && i + 1 < s.Length && Uri.IsHexDigit(s[i + 1]))
                            {
                                v = v * 16 + Convert.ToInt32(s[++i].ToString(), 16);
                                ++n;
                            }
                            o.Append((char)v);
                        }
                        break;
                    default:
                        if (c >= '0' && c <= '7')
                        {
                            int v = c - '0', n = 1;
                            while (n < 3 && i + 1 < s.Length && s[i + 1] >= '0' && s[i + 1] <= '7')
                            {
                                v = v * 8 + (s[++i] - '0');
                                ++n;
                            }
                            o.Append((char)v);
                        }
                        else
                        {
                            o.Append(c);
                        }
                        break;
                }
            }
            return o.ToString();
        }
    }
}
