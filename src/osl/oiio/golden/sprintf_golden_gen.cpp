// (not in OpenShadingLanguage) sprintf_golden_gen.cpp: writes the golden rows of sprintf_golden_test.cs by calling OIIO::Strutil::sprintf (tinyformat), which
// OSL's format() constant folder uses. Build:
//   g++ -std=c++17 -I<oiio-install>/include sprintf_golden_gen.cpp -L<oiio-install>/lib -lOpenImageIO -o gen && ./gen | gzip -9 > sprintf_golden.txt.gz
// Row format:  printf-format|K|value|expected    K = F float (IEEE bits in hex), I int, S string.   '|' and newline are written as \p and \n.
#include <OpenImageIO/strutil.h>
#include <cstdio>
#include <cstdint>
#include <cstring>
#include <limits>
#include <string>
#include <vector>
static std::string esc(const std::string& s)
{
    std::string o;
    for (char c : s) {
        if (c == '\n') o += "\\n";
        else if (c == '|') o += "\\p";
        else o += c;
    }
    return o;
}
int main()
{
    std::vector<std::string> fmts;
    const char* flags[]  = { "", "-", "+", " ", "#", "0", "-+", "+0", "#0", "- " };
    const char* widths[] = { "", "6", "12" };
    const char* precs[]  = { "", ".0", ".2", ".6", ".10" };
    const char* convs[]  = { "d", "i", "u", "x", "X", "o", "c", "e", "E", "f", "F", "g", "G", "s" };
    for (auto f : flags)
        for (auto w : widths)
            for (auto p : precs)
                for (auto c : convs)
                    fmts.push_back(std::string("%") + f + w + p + c);
    fmts.push_back("%%");
    fmts.push_back("a%%b%dc");
    fmts.push_back("%*d");
    fmts.push_back("%-*d;");
    fmts.push_back("%.*f");
    fmts.push_back("%ld");
    fmts.push_back("%lld");
    fmts.push_back("%hd");
    fmts.push_back("%5.2lf");
    std::vector<float> fv = { 0.f, -0.f, 1.f, -1.f, 0.5f, 0.1f, 3.14159265f, 100.f, 123456.789f, 1234567.f, 1e-5f, 0.0001f, 1e10f,
                              1e16f, -2.5f, 2.5f, 0.125f, 99999.5f, 999999.5f, 0.999999f, 3.4e38f, 1e-45f,
                              std::numeric_limits<float>::infinity(), -std::numeric_limits<float>::infinity(),
                              std::numeric_limits<float>::quiet_NaN() };
    std::vector<int> iv = { 0, 1, -1, 42, -42, 255, 65, 100000, 2147483647, (int)0x80000000 };
    std::vector<std::string> sv = { "", "hello", "hello world, this is long text", "%d" };
    // fmt::sprintf asserts (and the process aborts) when the conversion does not fit the argument type, so only the usable pairs are generated:
    // integers with d i u x X o c, floats with e E f F g G, strings with s ('%s' with a number is checked by the caller, see the notes below)
    auto last = [](const std::string& f) { return f.back(); };
    for (auto& f : fmts) {
        if (f == "%*d" || f == "%-*d;" || f == "%.*f")
            continue;
        char conv = last(f);
        bool intconv = std::string("diuxXoc").find(conv) != std::string::npos;
        bool fltconv = std::string("eEfFgG").find(conv) != std::string::npos;
        if (f == "%%" || f == "a%%b%dc" || f == "%ld" || f == "%lld" || f == "%hd")
            intconv = true;
        if (f == "%5.2lf")
            fltconv = true;
        if (intconv)
            for (int v : iv)
                printf("%s|I|%d|%s\n", f.c_str(), v, esc(OIIO::Strutil::sprintf(f.c_str(), v)).c_str());
        if (fltconv)
            for (float v : fv) {
                uint32_t b;
                memcpy(&b, &v, 4);
                printf("%s|F|%08x|%s\n", f.c_str(), b, esc(OIIO::Strutil::sprintf(f.c_str(), v)).c_str());
            }
        if (conv == 's')
            for (auto& v : sv)
                printf("%s|S|%s|%s\n", f.c_str(), esc(v).c_str(), esc(OIIO::Strutil::sprintf(f.c_str(), v)).c_str());
    }
    printf("%%*d|I|42|%s\n", esc(OIIO::Strutil::sprintf("%*d", 8, 42)).c_str());
    printf("%%-*d;|I|42|%s\n", esc(OIIO::Strutil::sprintf("%-*d;", 8, 42)).c_str());
    return 0;
}
