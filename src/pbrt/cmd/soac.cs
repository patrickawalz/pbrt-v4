// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// cmd/soac.cpp

/*
TODO:
- how to do float4, fancy packing tricks?
  flat int:32;
  float float:32
  struct Foo { int a, b; float c, d; };
  would be nice to load as a big float4...

- mechanism to not store fields that are easily recomputed...
  maybe the answer is to just do that--recompute only when needed--in the
  original struct!
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

public sealed class OptionalString
{
    public OptionalString() { }
    public OptionalString(string s) { this.s = s; set = true; }

    public static implicit operator bool(OptionalString o) { return o.set; }
    public static implicit operator string(OptionalString o)
    {
        Debug.Assert(o.set);
        return o.s;
    }
    public static bool operator ==(OptionalString o, string str)
    {
        Debug.Assert(o.set);
        return o.s == str;
    }
    public static bool operator !=(OptionalString o, string str)
    {
        Debug.Assert(o.set);
        return o.s != str;
    }
    public override bool Equals(object obj) { return base.Equals(obj); }
    public override int GetHashCode() { return base.GetHashCode(); }

    public string s = "";
    public bool set = false;
}

public sealed class Member
{
    public string type = "";
    public bool isConst = false;
    public int numPointers = 0;

    public string GetType()
    {
        string s = "";
        if (isConst)
            s = "const ";
        s += type;
        for (int i = 0; i < numPointers; ++i)
            s += "*";
        return s;
    }

    public List<string> names = new List<string>();
    public List<string> arraySizes = new List<string>();
}

public sealed class SOA
{
    public string type = "";
    public string templateType = "";
    public List<Member> members = new List<Member>();
}

public static class SoacMain
{
    static int line = 1;
    static string filename = null;
    static StringBuilder stdout = new StringBuilder();

    static void error(string fmt, params object[] args)
    {
        // fprintf(stderr, "%s:%d: ", filename, line); fprintf(stderr, fmt, args...); exit(1);
        Console.Error.Write((filename ?? "(null)") + ":" + line + ": ");
        Console.Error.Write(Format(fmt, args));
        Environment.Exit(1);
    }

    // Minimal printf: only %s is used by soac.
    static string Format(string fmt, object[] args)
    {
        StringBuilder sb = new StringBuilder();
        int a = 0;
        for (int i = 0; i < fmt.Length; ++i)
        {
            if (fmt[i] == '%' && i + 1 < fmt.Length && fmt[i + 1] == 's')
            {
                sb.Append(args[a++]);
                ++i;
            }
            else
                sb.Append(fmt[i]);
        }
        return sb.ToString();
    }

    static void printf(string fmt, params object[] args)
    {
        stdout.Append(Format(fmt, args));
    }

    static bool isspace(char c) { return c == ' ' || (c >= '\t' && c <= '\r'); }
    static bool isalpha(char c) { return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'); }
    static bool isalnum(char c) { return isalpha(c) || (c >= '0' && c <= '9'); }

    static string fileContents;
    static int pos = 0;

    static bool eof() { return pos == fileContents.Length; }
    static char getc()
    {
        Debug.Assert(!eof());
        if (fileContents[pos] == '\n')
            ++line;
        return fileContents[pos++];
    }
    static void ungetc()
    {
        Debug.Assert(pos > 0);
        --pos;
        if (fileContents[pos] == '\n')
            --line;
    }

    static OptionalString getToken(bool eofOk)
    {
        if (eof())
        {
            if (eofOk)
                return new OptionalString();
            else
                error("Premature end of file.\n");
        }

        // Skip whitespace
        while (true)
        {
            if (eof())
            {
                if (eofOk)
                    return new OptionalString();
                else
                    error("Premature end of file.\n");
            }

            char c = getc();
            if (!isspace(c))
            {
                ungetc();
                break;
            }
        }

        Debug.Assert(!eof());
        string s = "";
        s += getc();
        if (s[0] == '/' && !eof())
        {
            if (getc() == '/')
            {
                // skip to EOL
                while (true)
                {
                    if (eof())
                    {
                        if (eofOk)
                            return new OptionalString();
                        else
                            error("Premature end of file.\n");
                    }
                    if (getc() == '\n')
                        return getToken(eofOk);
                }
            }
            else
                ungetc();
        }
        if (!isalpha(s[0]) && s[0] != '_')
            return new OptionalString(s);

        while (!eof())
        {
            char c = getc();
            if (!isalnum(c) && c != '_')
            {
                // end of token
                ungetc();
                break;
            }
            s += c;
        }
        return new OptionalString(s);
    }

    static HashSet<string> flatTypes = new HashSet<string>(StringComparer.Ordinal);
    static HashSet<string> externSOA = new HashSet<string>(StringComparer.Ordinal);
    static bool isFlatType(string type)
    {
        return flatTypes.Contains(type);
    }

    // keep as a vector so that we can emit them in the order they were
    // defined.
    static List<SOA> soaTypes = new List<SOA>();

    static bool soaTypeExists(string type)
    {
        foreach (SOA s in soaTypes)
            if (s.type == type)
                return true;
        return externSOA.Contains(type);
    }

    static void expect(string str)
    {
        OptionalString tok = getToken(true);
        if (!tok)
            error("Premature end of file; expected \"%s\".\n", str);
        if (tok != str)
            error("Syntax error: expected \"%s\".\n", str);
    }

    public static int Main(string[] argv)
    {
        if (argv.Length != 1)
            error("usage: soac <soac filename>\n");

        // Read the file
        filename = argv[0];
        try
        {
            // (std::ifstream in binary mode; bytes map one-to-one to chars.)
            fileContents = Encoding.Latin1.GetString(File.ReadAllBytes(filename));
        }
        catch (Exception)
        {
            error("%s: %s", filename, "No such file or directory");
            return 0;
        }

        while (true)
        {
            OptionalString os = getToken(true);
            if (!os)
                break;
            string tok = os.s;
            if (tok == "flat")
            {
                OptionalString typeTok = getToken(false);

                string type = typeTok;
                if (flatTypes.Contains(type))
                    error("%s flat type redeclared.\n", type);
                flatTypes.Add(type);

                expect(";");
            }
            else if (tok == "soa")
            {
                SOA soa = new SOA();

                OptionalString typeTok = getToken(false);
                soa.type = (string)typeTok;
                if (!isalpha(soa.type[0]))
                    error("%s: invalid type identifier.\n", soa.type);

                if (soaTypeExists(soa.type))
                    error("%s: type redefined.\n", soa.type);

                OptionalString tok2 = getToken(false);
                if (tok2 == "<")
                {
                    tok2 = getToken(false);
                    soa.templateType = (string)tok2;
                    if (!isalpha(soa.templateType[0]))
                        error("%s: invalid type identifier.\n", soa.templateType);
                    expect(">");
                    expect("{");
                }
                else if (tok2 == ";")
                {
                    externSOA.Add(soa.type);
                    continue;
                }
                else if (tok2 != "{")
                    error("Syntax error: expected \"{\".\n");

                while (true)
                {
                    OptionalString tok3 = getToken(false);
                    if (tok3 == "}")
                        break;

                    Member member = new Member();
                    member.type = (string)tok3;
                    // Hacks to parse things like const Foo *
                    if (member.type == "const")
                    {
                        member.isConst = true;
                        tok3 = getToken(false);
                        member.type = (string)tok3;
                    }
                    while (true)
                    {
                        tok3 = getToken(false);
                        if (tok3 == "*")
                            ++member.numPointers;
                        else
                            break;
                    }

                    // Don't check the type if it's a pointer; we already know
                    // how to SOA pointers..
                    if (member.numPointers == 0 && member.type != soa.templateType &&
                        !flatTypes.Contains(member.type) &&
                        !soaTypeExists(member.type))
                        error("%s: undefined type\n", member.type);

                    while (true)
                    {
                        string memberName = tok3;
                        member.names.Add(memberName);
                        member.arraySizes.Add("");  // assume no array for starters

                        tok3 = getToken(false);
                        if (tok3 == "[")
                        {
                            tok3 = getToken(false);
                            // just pass it through without interpretation
                            member.arraySizes[member.arraySizes.Count - 1] =
                                (string)tok3;
                            expect("]");
                            tok3 = getToken(false);
                        }

                        if (tok3 == ";")
                            break;
                        else if (tok3 == ",")
                            tok3 = getToken(false);  // and go around again...
                    }

                    if (member.names.Count == 0)
                        error("No members specified after type declaration.\n");
                    soa.members.Add(member);
                }
                expect(";");

                soaTypes.Add(soa);
            }
            else
                error("%s: invalid token", tok);
        }

    // And now emit them...
    printf("// SOA definitions automatically generated by soac\n");
    printf("// DO NOT EDIT THIS FILE MANUALLY\n\n");
    printf("template <typename T> struct SOA;\n\n");
    foreach (SOA soa in soaTypes) {
        if (soa.templateType.Length != 0)
            printf("template <typename %s> struct SOA<%s<%s>> {\n",
                   soa.templateType, soa.type, soa.templateType);
        else
            printf("template <> struct SOA<%s> {\n", soa.type);

        // Constructor
        printf("    SOA() = default;\n");
        printf("    SOA(int n, Allocator alloc) : nAlloc(n) {\n");
        foreach (Member member in soa.members) {
            for (int i = 0; i < member.names.Count; ++i) {
                string name = member.names[i];
                if (member.arraySizes[i].Length != 0) {
                    printf("        for (int i = 0; i < %s; ++i)\n",
                           member.arraySizes[i]);
                    if (isFlatType(member.type) || member.numPointers > 0)
                        printf(
                            "            this->%s[i] = alloc.allocate_object<%s>(n);\n",
                            name, member.GetType());
                    else {
                        Debug.Assert(member.isConst == false && member.numPointers == 0);
                        printf("        this->%s[i] = SOA<%s>(n, alloc);\n", name,
                               member.type);
                    }
                } else {
                    if (isFlatType(member.type) || member.numPointers > 0)
                        printf("        this->%s = alloc.allocate_object<%s>(n);\n",
                               name, member.GetType());
                    else
                        printf("        this->%s = SOA<%s>(n, alloc);\n", name,
                               member.type);
                }
            }
        }
        printf("    }\n");
        printf("    SOA &operator=(const SOA& s) {\n");
        printf("        nAlloc = s.nAlloc;\n");
        foreach (Member member in soa.members) {
            for (int i = 0; i < member.names.Count; ++i) {
                string name = member.names[i];
                if (member.arraySizes[i].Length != 0) {
                    printf("        for (int i = 0; i < %s; ++i)\n",
                           member.arraySizes[i]);
                    printf("            this->%s[i] = s.%s[i];\n", name,
                           name);
                } else {
                    printf("        this->%s = s.%s;\n", name, name);
                }
            }
        }
        printf("        return *this;\n");
        printf("    }\n");

        // operator[] madness...
        printf("    struct GetSetIndirector {\n");
        if (soa.templateType.Length != 0) {
            printf("        PBRT_CPU_GPU\n");
            printf("        operator %s<%s>() const {\n", soa.type,
                   soa.templateType);
            printf("            %s<%s> r;\n", soa.type, soa.templateType);
        } else {
            printf("        PBRT_CPU_GPU\n");
            printf("        operator %s() const {\n", soa.type);
            printf("            %s r;\n", soa.type);
        }
        foreach (Member member in soa.members)
            for (int i = 0; i < member.names.Count; ++i) {
                string name = member.names[i];
                if (member.arraySizes[i].Length != 0) {
                    printf("            for (int c = 0; c < %s; ++c)\n",
                           member.arraySizes[i]);
                    printf("                r.%s[c] = soa->%s[c][i];\n", name,
                           name);
                } else
                    printf("            r.%s = soa->%s[i];\n", name,
                           name);
            }
        printf("            return r;\n");
        printf("        }\n");

        printf("        PBRT_CPU_GPU\n");
        if (soa.templateType.Length != 0)
            printf("        void operator=(const %s<%s> &a) {\n", soa.type,
                   soa.templateType);
        else
            printf("        void operator=(const %s &a) {\n", soa.type);
        foreach (Member member in soa.members)
            for (int i = 0; i < member.names.Count; ++i) {
                string name = member.names[i];
                if (member.arraySizes[i].Length != 0) {
                    printf("            for (int c = 0; c < %s; ++c)\n",
                           member.arraySizes[i]);
                    printf("                soa->%s[c][i] = a.%s[c];\n", name,
                           name);
                } else
                    printf("            soa->%s[i] = a.%s;\n", name,
                           name);
            }
        printf("        }\n\n");
        printf("        SOA *soa;\n");
        printf("        int i;\n");
        printf("    };\n\n");

        printf("    PBRT_CPU_GPU\n");
        printf("    GetSetIndirector operator[](int i) {\n");
        printf("        DCHECK_LT(i, nAlloc);\n");
        printf("        return GetSetIndirector{this, i};\n");
        printf("    }\n");
        printf("    PBRT_CPU_GPU\n");
        if (soa.templateType.Length != 0) {
            printf("    %s<%s> operator[](int i) const {\n", soa.type,
                   soa.templateType);
            printf("        DCHECK_LT(i, nAlloc);\n");
            printf("        %s<%s> r;\n", soa.type, soa.templateType);
        } else {
            printf("    %s operator[](int i) const {\n", soa.type);
            printf("        DCHECK_LT(i, nAlloc);\n");
            printf("        %s r;\n", soa.type);
        }
        foreach (Member member in soa.members)
            for (int i = 0; i < member.names.Count; ++i) {
                string name = member.names[i];
                if (member.arraySizes[i].Length != 0) {
                    printf("        for (int c = 0; c < %s; ++c)\n",
                           member.arraySizes[i]);
                    printf("            r.%s[c] = this->%s[c][i];\n", name,
                           name);
                } else
                    printf("        r.%s = this->%s[i];\n", name, name);
            }
        printf("        return r;\n");
        printf("    }\n");
        printf("\n");

        // Member definitions
        printf("    int nAlloc;\n");
        foreach (Member member in soa.members) {
            for (int i = 0; i < member.names.Count; ++i) {
                string name = member.names[i];
                if (member.arraySizes[i].Length != 0) {
                    if (isFlatType(member.type) || member.numPointers > 0)
                        printf("    %s * /*PBRT_RESTRICT*/ %s[%s];\n",
                               member.GetType(), name,
                               member.arraySizes[i]);
                    else
                        printf("    SOA<%s> %s[%s];\n", member.type, name,
                               member.arraySizes[i]);
                } else {
                    if (isFlatType(member.type) || member.numPointers > 0)
                        printf("    %s * PBRT_RESTRICT %s;\n", member.GetType(),
                               name);
                    else
                        printf("    SOA<%s> %s;\n", member.type, name);
                }
            }
        }

        printf("};\n\n");
    }
        Console.Out.Write(stdout.ToString());
        Console.Out.Flush();
        return 0;
    }
}
