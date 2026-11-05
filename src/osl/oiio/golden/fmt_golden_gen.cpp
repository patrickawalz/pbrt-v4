// (not in OpenShadingLanguage) fmt_golden_gen.cpp: writes the golden rows of fmt_golden_test.cs by formatting with the {fmt} library that OIIO bundles
// (the one OSL's decode_message calls). Build:  g++ -std=c++17 -I<oiio-install>/include fmt_golden_gen.cpp -o gen && ./gen | gzip -9 > fmt_golden.txt.gz
// Row format:  K|spec|value|expected   K = F float (value = IEEE bits in hex), D double (bits), I int, U unsigned, S string;
// "EXC:message" as the expected text means {fmt} threw std::format_error with that message. '|' and newline in text are written as \p and \n.
#include <cstdio>
#include <cstring>
#include <cstdint>
#include <string>
#include <vector>
#include <cmath>
#include <limits>
#define FMT_HEADER_ONLY
#include <OpenImageIO/detail/fmt/format.h>
static std::string esc(const std::string& s){ std::string o; for(char c: s){ if(c=='\n') o+="\\n"; else if(c=='|') o+="\\p"; else o+=c;} return o; }
int main(){
  std::vector<std::string> fspecs, ispecs, sspecs;
  const char* flags[] = {"", "+", " ", "<", ">", "^", "#", "0", "+0", "#0"};
  const char* widths[] = {"", "7", "12"};
  const char* precs[] = {"", ".0", ".2", ".6", ".9"};
  const char* ftypes[] = {"", "f","e","g","E","G"};
  for(auto f:flags) for(auto w:widths) for(auto p:precs) for(auto t:ftypes){ std::string s=std::string(f)+w+p+t; fspecs.push_back(s);}
  const char* itypes[] = {"", "d","x","X","o","b"};
  const char* iprecs[] = {""};
  for(auto f:flags) for(auto w:widths) for(auto t:itypes){ std::string s=std::string(f)+w+t; ispecs.push_back(s);}
  const char* stypes[] = {"", "s"};
  const char* sflags[] = {"", "<", ">", "^"};
  const char* sprecs[] = {"", ".0", ".2", ".5", ".20"};
  for(auto f:sflags) for(auto w:widths) for(auto p:sprecs) for(auto t:stypes){ sspecs.push_back(std::string(f)+w+p+t);}
  std::vector<float> fv = {0.f,-0.f,1.f,-1.f,0.1f,3.14159265f,100.f,123456.789f,1234567.f,1.5e-4f,0.0001f,1e10f,1e16f,-2.5f,0.125f,99999.5f,999999.5f,0.999999f,3.4e38f,1e-45f,
    std::numeric_limits<float>::infinity(), -std::numeric_limits<float>::infinity(), std::numeric_limits<float>::quiet_NaN(), 0.3f, 2.0f/3.0f, 1.0f/3.0f, 5e-5f, 12345.6789f};
  std::vector<double> dv = {0.1,1e100,2.0/3.0,1e-300};
  std::vector<int> iv = {0,-1,42,-42,255,2147483647,(int)0x80000000};
  std::vector<uint32_t> uv = {0,255,0xdeadbeefu};
  std::vector<std::string> sv = {"", "hello", "hello world, this is long text", "{}"};
  const char* weird[] = {"u","5.2d","+s","05s","#s","s","d","f","5.2s",".3","<5.2d","+c","e","g","x",".0x","#o","#b","10.3x","c","X","-5","+","+d","06d","<06d","^+8.2f","*^9d","_<7s","z>6","{","}","L","5L","2$","s}"};
  for(auto w: weird){ std::string spec=w;
    { std::string r; try{ r=fmt::format(fmt::runtime("{:"+spec+"}"), 7);}catch(std::exception& e){ r=std::string("EXC:")+e.what(); } printf("I|%s|7|%s\n", spec.c_str(), esc(r).c_str()); }
    { std::string r; try{ r=fmt::format(fmt::runtime("{:"+spec+"}"), 2.5f);}catch(std::exception& e){ r=std::string("EXC:")+e.what(); } uint32_t b; float vv=2.5f; memcpy(&b,&vv,4); printf("F|%s|%08x|%s\n", spec.c_str(), b, esc(r).c_str()); }
    { std::string r; try{ r=fmt::format(fmt::runtime("{:"+spec+"}"), "abc");}catch(std::exception& e){ r=std::string("EXC:")+e.what(); } printf("S|%s|abc|%s\n", spec.c_str(), esc(r).c_str()); }
    { std::string r; try{ r=fmt::format(fmt::runtime("{:"+spec+"}"), 4000000000u);}catch(std::exception& e){ r=std::string("EXC:")+e.what(); } printf("U|%s|4000000000|%s\n", spec.c_str(), esc(r).c_str()); }
  }
  for(auto& s: fspecs) for(float v: fv){ uint32_t b; memcpy(&b,&v,4); std::string r; try{ r=fmt::format(fmt::runtime("{:"+s+"}"), v);}catch(std::exception& e){ r=std::string("EXC:")+e.what(); } printf("F|%s|%08x|%s\n", s.c_str(), b, esc(r).c_str()); }
  for(auto& s: fspecs) for(double v: dv){ uint64_t b; memcpy(&b,&v,8); std::string r; try{ r=fmt::format(fmt::runtime("{:"+s+"}"), v);}catch(std::exception& e){ r=std::string("EXC:")+e.what(); } printf("D|%s|%016llx|%s\n", s.c_str(), (unsigned long long)b, esc(r).c_str()); }
  for(auto& s: ispecs) for(int v: iv){ std::string r; try{ r=fmt::format(fmt::runtime("{:"+s+"}"), v);}catch(std::exception& e){ r=std::string("EXC:")+e.what(); } printf("I|%s|%d|%s\n", s.c_str(), v, esc(r).c_str()); }
  for(auto& s: ispecs) for(uint32_t v: uv){ std::string r; try{ r=fmt::format(fmt::runtime("{:"+s+"}"), v);}catch(std::exception& e){ r=std::string("EXC:")+e.what(); } printf("U|%s|%u|%s\n", s.c_str(), v, esc(r).c_str()); }
  for(auto& s: sspecs) for(auto& v: sv){ std::string r; try{ r=fmt::format(fmt::runtime("{:"+s+"}"), v.c_str());}catch(std::exception& e){ r=std::string("EXC:")+e.what(); } printf("S|%s|%s|%s\n", s.c_str(), esc(v).c_str(), esc(r).c_str()); }
}
