// (not in OpenShadingLanguage) Minimal oiiotool/maketx for the testsuite's texture generation, built on libOpenImageIO so that the generated
// textures (mip levels, tx metadata) are exactly what the real tools write. Supports the command lines the testsuite's run.py files use:
//   oiiotool [--no-clobber] -pattern constant:color=r,g,b[,a] | fill:topleft=..:topright=..:bottomleft=..:bottomright=.. | checker WxH[+X+Y] NCH
//            [-fullsize WxH+X+Y] [-text:size=S:x=X:y=Y STR] [-d TYPE] (-o FILE | -otex FILE | -oenv FILE)
//   maketx INPUT [--wrap MODE] -o OUTPUT
#include <OpenImageIO/imagebuf.h>
#include <OpenImageIO/imagebufalgo.h>
#include <OpenImageIO/strutil.h>
#include <OpenImageIO/filesystem.h>
#include <cstdio>
#include <cstdlib>
#include <string>
#include <vector>
using namespace OIIO;

static std::vector<float> parse_floats(const std::string& s)
{
    std::vector<float> v;
    for (auto& p : Strutil::splits(s, ","))
        v.push_back(Strutil::stof(p));
    return v;
}

static void parse_geom(const std::string& s, int& w, int& h, int& x, int& y)
{
    w = h = 0; x = y = 0;
    std::sscanf(s.c_str(), "%dx%d%d%d", &w, &h, &x, &y);
}

static TypeDesc parse_type(const std::string& s)
{
    if (s == "uint8") return TypeDesc::UINT8;
    if (s == "uint16") return TypeDesc::UINT16;
    if (s == "half") return TypeDesc::HALF;
    if (s == "float") return TypeDesc::FLOAT;
    if (s == "int8") return TypeDesc::INT8;
    if (s == "int16") return TypeDesc::INT16;
    if (s == "uint32") return TypeDesc::UINT32;
    if (s == "int32") return TypeDesc::INT32;
    return TypeDesc::UNKNOWN;
}

int main(int argc, char** argv)
{
    std::string prog = Filesystem::filename(argv[0]);
    if (prog.find("maketx") != std::string::npos) {
        std::string in, out, wrap;
        for (int i = 1; i < argc; ++i) {
            std::string a = argv[i];
            if (a == "-o" && i + 1 < argc) out = argv[++i];
            else if (a == "--wrap" && i + 1 < argc) wrap = argv[++i];
            else if (a[0] != '-') in = a;
        }
        ImageSpec config;
        if (!wrap.empty())
            config.attribute("wrapmodes", wrap + "," + wrap);
        bool ok = ImageBufAlgo::make_texture(ImageBufAlgo::MakeTxTexture, in, out, config, &std::cout);
        if (!ok) { std::fprintf(stderr, "maketx: failed\n"); return 1; }
        return 0;
    }
    ImageBuf cur;
    TypeDesc fmt = TypeDesc::UNKNOWN;
    bool noclobber = false;
    for (int i = 1; i < argc; ++i) {
        std::string a = argv[i];
        while (!a.empty() && a[0] == '-') a.erase(0, 1);
        std::string opt = a.substr(0, a.find(':'));
        if (opt == "no-clobber") {
            noclobber = true;
        } else if (opt == "pattern") {
            std::string pat = argv[++i], geom = argv[++i];
            int nch = std::atoi(argv[++i]);
            int w, h, x, y;
            parse_geom(geom, w, h, x, y);
            ImageSpec spec(w, h, nch, TypeDesc::FLOAT);
            spec.x = x; spec.y = y;
            spec.full_x = x; spec.full_y = y; spec.full_width = w; spec.full_height = h;
            cur.reset(spec);
            std::string name = pat.substr(0, pat.find(':'));
            std::vector<std::string> kv = Strutil::splits(pat, ":");
            std::vector<float> c1(nch, 0.0f), c2(nch, 1.0f), color(nch, 0.0f);
            int cw = 8, ch = 8;
            std::vector<float> tl(nch, 0.0f), tr(nch, 0.0f), bl(nch, 0.0f), br(nch, 0.0f);
            for (size_t k = 1; k < kv.size(); ++k) {
                size_t eq = kv[k].find('=');
                std::string key = kv[k].substr(0, eq), val = kv[k].substr(eq + 1);
                std::vector<float> f = parse_floats(val);
                f.resize(nch, 0.0f);
                if (key == "color") color = f;
                else if (key == "color1") c1 = f;
                else if (key == "color2") c2 = f;
                else if (key == "width") cw = std::atoi(val.c_str());
                else if (key == "height") ch = std::atoi(val.c_str());
                else if (key == "topleft") tl = f;
                else if (key == "topright") tr = f;
                else if (key == "bottomleft") bl = f;
                else if (key == "bottomright") br = f;
            }
            if (name == "constant") ImageBufAlgo::fill(cur, color);
            else if (name == "fill") ImageBufAlgo::fill(cur, tl, tr, bl, br);
            else if (name == "checker") ImageBufAlgo::checker(cur, cw, ch, 1, c1, c2, 0, 0, 0);
            else { std::fprintf(stderr, "oiiotool: unknown pattern %s\n", name.c_str()); return 1; }
        } else if (opt == "fullsize") {
            int w, h, x, y;
            parse_geom(argv[++i], w, h, x, y);
            ImageSpec& s = cur.specmod();
            s.full_x = x; s.full_y = y; s.full_width = w; s.full_height = h;
        } else if (opt == "text") {
            int size = 16, x = 0, y = 0;
            std::vector<std::string> kv = Strutil::splits(argv[i], ":");
            for (size_t k = 1; k < kv.size(); ++k) {
                size_t eq = kv[k].find('=');
                std::string key = kv[k].substr(0, eq);
                int val = std::atoi(kv[k].c_str() + eq + 1);
                if (key == "size") size = val; else if (key == "x") x = val; else if (key == "y") y = val;
            }
            std::string text = argv[++i];
            ImageBufAlgo::render_text(cur, x, y, text, size);
        } else if (opt == "d") {
            fmt = parse_type(argv[++i]);
        } else if (opt == "o" || opt == "otex" || opt == "oenv") {
            std::string file = argv[++i];
            if (noclobber && Filesystem::exists(file)) continue;
            bool ok;
            if (opt == "o") {
                ok = fmt != TypeDesc::UNKNOWN ? cur.write(file, fmt) : cur.write(file);
            } else {
                ImageSpec config;
                if (fmt != TypeDesc::UNKNOWN) config.format = fmt;
                ok = ImageBufAlgo::make_texture(opt == "otex" ? ImageBufAlgo::MakeTxTexture : ImageBufAlgo::MakeTxEnvLatl, cur, file, config, &std::cout);
            }
            if (!ok) { std::fprintf(stderr, "oiiotool: could not write %s: %s\n", file.c_str(), cur.geterror().c_str()); return 1; }
        } else {
            // the oslbody / ?RES= forms of the osl.imageio plugin are not supported
            std::fprintf(stderr, "oiiotool: unsupported argument %s\n", argv[i]);
            return 1;
        }
    }
    return 0;
}
