// osd_oracle.cpp: dumps what OpenSubdiv computes for a mesh (refinement, patch table, stencils, limit samples) as text, so that the
// C# port can be compared with it. Build (against a native OpenSubdiv build): see README.md.
//
// Usage: osd_oracle mesh.txt        (mesh.txt: see parseMesh)
#include <opensubdiv/far/topologyDescriptor.h>
#include <opensubdiv/far/primvarRefiner.h>
#include <opensubdiv/far/patchTableFactory.h>
#include <opensubdiv/far/patchMap.h>
#include <opensubdiv/far/ptexIndices.h>
#include <opensubdiv/far/stencilTableFactory.h>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>
#include <map>

using namespace OpenSubdiv;
typedef double Real;

struct Mesh {
    std::string scheme = "catmark";
    std::map<std::string, int> opt;          // integer options
    std::vector<Real> verts;
    std::vector<int> vertsPerFace, faceVerts;
    std::vector<int> creaseVerts; std::vector<float> creaseWeights;
    std::vector<int> cornerVerts; std::vector<float> cornerWeights;
    std::vector<int> holes;
};

static Mesh parseMesh(char const * name) {
    Mesh m; std::ifstream in(name); std::string line;
    while (std::getline(in, line)) {
        std::istringstream s(line); std::string key; s >> key;
        if (key.empty() || key[0] == '#') continue;
        if (key == "scheme") s >> m.scheme;
        else if (key == "opt") { std::string k; int v; s >> k >> v; m.opt[k] = v; }
        else if (key == "v") { Real x, y, z; s >> x >> y >> z; m.verts.push_back(x); m.verts.push_back(y); m.verts.push_back(z); }
        else if (key == "f") { int i, n = 0; while (s >> i) { m.faceVerts.push_back(i); ++n; } m.vertsPerFace.push_back(n); }
        else if (key == "crease") { int a, b; float w; s >> a >> b >> w; m.creaseVerts.push_back(a); m.creaseVerts.push_back(b); m.creaseWeights.push_back(w); }
        else if (key == "corner") { int a; float w; s >> a >> w; m.cornerVerts.push_back(a); m.cornerWeights.push_back(w); }
        else if (key == "hole") { int a; s >> a; m.holes.push_back(a); }
    }
    return m;
}

static int get(Mesh const & m, char const * k, int def) { auto it = m.opt.find(k); return it == m.opt.end() ? def : it->second; }

static void dumpIndices(char const * tag, Far::ConstIndexArray a) {
    printf("%s", tag); for (int i = 0; i < a.size(); ++i) printf(" %d", a[i]); printf("\n");
}
static void dumpLocalIndices(char const * tag, Far::ConstLocalIndexArray a) {
    printf("%s", tag); for (int i = 0; i < a.size(); ++i) printf(" %d", (int)a[i]); printf("\n");
}

static void dumpLevel(Far::TopologyLevel const & L, int level) {
    printf("level %d verts %d edges %d faces %d faceverts %d\n", level, L.GetNumVertices(), L.GetNumEdges(), L.GetNumFaces(),
           L.GetNumFaceVertices());
    for (int f = 0; f < L.GetNumFaces(); ++f) {
        dumpIndices("fv", L.GetFaceVertices(f)); dumpIndices("fe", L.GetFaceEdges(f));
        printf("fhole %d\n", (int)L.IsFaceHole(f));
    }
    for (int e = 0; e < L.GetNumEdges(); ++e) {
        dumpIndices("ev", L.GetEdgeVertices(e)); dumpIndices("ef", L.GetEdgeFaces(e));
        printf("esharp %.9g nonmanifold %d boundary %d infsharp %d semisharp %d\n", L.GetEdgeSharpness(e), (int)L.IsEdgeNonManifold(e),
               (int)L.IsEdgeBoundary(e), (int)L.IsEdgeInfSharp(e), (int)L.IsEdgeSemiSharp(e));
    }
    for (int v = 0; v < L.GetNumVertices(); ++v) {
        dumpIndices("ve", L.GetVertexEdges(v)); dumpIndices("vf", L.GetVertexFaces(v)); dumpLocalIndices("vfl", L.GetVertexFaceLocalIndices(v));
        printf("vsharp %.9g nonmanifold %d boundary %d corner %d regular %d infsharp %d semisharp %d rule %d\n", L.GetVertexSharpness(v),
               (int)L.IsVertexNonManifold(v), (int)L.IsVertexBoundary(v), (int)L.IsVertexCorner(v), (int)L.IsVertexValenceRegular(v),
               (int)L.IsVertexInfSharp(v), (int)L.IsVertexSemiSharp(v), (int)L.GetVertexRule(v));
    }
}

static void dumpStencils(char const * tag, Far::StencilTableReal<Real> const * t) {
    if (!t) { printf("%s none\n", tag); return; }
    printf("%s stencils %d elements %d\n", tag, t->GetNumStencils(), (int)t->GetControlIndices().size());
    for (int i = 0; i < t->GetNumStencils(); ++i) {
        Far::StencilReal<Real> s = t->GetStencil(i);
        printf("st %d:", s.GetSize());
        for (int j = 0; j < s.GetSize(); ++j) printf(" %d %.9g", s.GetVertexIndices()[j], s.GetWeights()[j]);
        printf("\n");
    }
}

struct Vertex {
    Vertex() {}
    void Clear(void * = 0) { p[0] = p[1] = p[2] = 0; }
    void AddWithWeight(Vertex const & s, Real w) { p[0] += w * s.p[0]; p[1] += w * s.p[1]; p[2] += w * s.p[2]; }
    Real p[3];
};

int main(int argc, char ** argv) {
    if (argc < 2) return 1;
    Mesh m = parseMesh(argv[1]);
    Sdc::SchemeType scheme = m.scheme == "loop" ? Sdc::SCHEME_LOOP : m.scheme == "bilinear" ? Sdc::SCHEME_BILINEAR : Sdc::SCHEME_CATMARK;
    Sdc::Options sdcOptions;
    sdcOptions.SetVtxBoundaryInterpolation((Sdc::Options::VtxBoundaryInterpolation)get(m, "boundary", 1));
    sdcOptions.SetFVarLinearInterpolation((Sdc::Options::FVarLinearInterpolation)get(m, "fvarlinear", 1));
    sdcOptions.SetCreasingMethod((Sdc::Options::CreasingMethod)get(m, "creasing", 0));
    sdcOptions.SetTriangleSubdivision((Sdc::Options::TriangleSubdivision)get(m, "trisub", 0));

    typedef Far::TopologyDescriptor Descriptor;
    Descriptor desc;
    desc.numVertices = (int)m.verts.size() / 3; desc.numFaces = (int)m.vertsPerFace.size();
    desc.numVertsPerFace = &m.vertsPerFace[0]; desc.vertIndicesPerFace = &m.faceVerts[0];
    desc.numCreases = (int)m.creaseWeights.size();
    if (desc.numCreases) { desc.creaseVertexIndexPairs = &m.creaseVerts[0]; desc.creaseWeights = &m.creaseWeights[0]; }
    desc.numCorners = (int)m.cornerWeights.size();
    if (desc.numCorners) { desc.cornerVertexIndices = &m.cornerVerts[0]; desc.cornerWeights = &m.cornerWeights[0]; }
    desc.numHoles = (int)m.holes.size(); if (desc.numHoles) desc.holeIndices = &m.holes[0];

    Far::TopologyRefiner * refiner = Far::TopologyRefinerFactory<Descriptor>::Create(desc, Far::TopologyRefinerFactory<Descriptor>::Options(scheme, sdcOptions));
    printf("scheme %s\n", m.scheme.c_str());

    int uniform = get(m, "uniform", 0);
    if (uniform) {
        Far::TopologyRefiner::UniformOptions uo(uniform);
        uo.fullTopologyInLastLevel = get(m, "fulltopo", 1);
        refiner->RefineUniform(uo);
        printf("uniform %d\n", uniform);
        for (int l = 0; l < refiner->GetNumLevels(); ++l) dumpLevel(refiner->GetLevel(l), l);
        // refine the vertex positions with the PrimvarRefiner and dump them
        std::vector<Vertex> verts(refiner->GetNumVerticesTotal());
        std::memcpy(&verts[0], &m.verts[0], m.verts.size() * sizeof(Real));
        Far::PrimvarRefinerReal<Real> pr(*refiner);
        Vertex * src = &verts[0];
        for (int l = 1; l < refiner->GetNumLevels(); ++l) {
            Vertex * dst = src + refiner->GetLevel(l - 1).GetNumVertices();
            pr.Interpolate(l, src, dst); src = dst;
        }
        for (size_t i = 0; i < verts.size(); ++i) printf("pt %.9g %.9g %.9g\n", verts[i].p[0], verts[i].p[1], verts[i].p[2]);
        // limit positions and tangents of the last level
        std::vector<Vertex> lp(refiner->GetLevel(refiner->GetNumLevels() - 1).GetNumVertices()), l1(lp.size()), l2(lp.size());
        Far::PrimvarRefinerReal<Real> pr2(*refiner);
        pr2.Limit(src, lp, l1, l2);
        for (size_t i = 0; i < lp.size(); ++i) printf("limit %.9g %.9g %.9g t1 %.9g %.9g %.9g t2 %.9g %.9g %.9g\n", lp[i].p[0], lp[i].p[1], lp[i].p[2],
               l1[i].p[0], l1[i].p[1], l1[i].p[2], l2[i].p[0], l2[i].p[1], l2[i].p[2]);
        return 0;
    }

    int level = get(m, "level", 3);
    Far::PatchTableFactory::Options po(level);
    po.SetPatchPrecision<Real>();
    po.useInfSharpPatch = get(m, "infsharp", 0);
    po.useSingleCreasePatch = get(m, "singlecrease", 0);
    po.generateVaryingTables = false;
    po.generateVaryingLocalPoints = false;
    po.SetEndCapType((Far::PatchTableFactory::Options::EndCapType)get(m, "endcap", 3));
    po.shareEndCapPatchPoints = get(m, "share", 1);
    po.generateLegacySharpCornerPatches = get(m, "legacycorner", 1);
    refiner->RefineAdaptive(po.GetRefineAdaptiveOptions());
    printf("adaptive %d levels %d\n", level, refiner->GetNumLevels());
    for (int l = 0; l < refiner->GetNumLevels(); ++l) dumpLevel(refiner->GetLevel(l), l);

    Far::PatchTable const * pt = Far::PatchTableFactory::Create(*refiner, po);
    printf("patchtable arrays %d patches %d cvs %d localpoints %d maxvalence %d\n", pt->GetNumPatchArrays(), pt->GetNumPatchesTotal(),
           pt->GetNumControlVerticesTotal(), pt->GetNumLocalPoints(), pt->GetMaxValence());
    for (int a = 0; a < pt->GetNumPatchArrays(); ++a) {
        Far::PatchDescriptor d = pt->GetPatchArrayDescriptor(a);
        printf("array %d type %d patches %d cvs %d\n", a, (int)d.GetType(), pt->GetNumPatches(a), d.GetNumControlVertices());
        for (int p = 0; p < pt->GetNumPatches(a); ++p) {
            Far::PatchParam pp = pt->GetPatchParam(a, p);
            printf("param face %d u %d v %d depth %d nonquad %d boundary %d trans %d reg %d\n", pp.GetFaceId(), pp.GetU(), pp.GetV(), pp.GetDepth(),
                   (int)pp.NonQuadRoot(), pp.GetBoundary(), pp.GetTransition(), (int)pp.IsRegular());
            dumpIndices("pcv", pt->GetPatchVertices(a, p));
        }
    }
    for (int a = 0; a < pt->GetNumPatchArrays(); ++a) {
        for (int p = 0; p < pt->GetNumPatches(a); ++p) {
            Far::PatchTable::PatchHandle h; h.arrayIndex = a; h.patchIndex = p; h.vertIndex = 0;
            (void)h;
        }
    }
    printf("quadoffsets %d\n", (int)pt->GetNumPatchesTotal());
    dumpStencils("localstencils", pt->GetLocalPointStencilTable<Real>());

    // limit samples on a fixed grid of every ptex face
    int nRefinerVertices = refiner->GetNumVerticesTotal(), nLocal = pt->GetNumLocalPoints();
    std::vector<Vertex> verts(nRefinerVertices + nLocal);
    std::memcpy(&verts[0], &m.verts[0], m.verts.size() * sizeof(Real));
    Far::PrimvarRefinerReal<Real> pr(*refiner);
    Vertex * src = &verts[0];
    for (int l = 1; l < refiner->GetNumLevels(); ++l) {
        Vertex * dst = src + refiner->GetLevel(l - 1).GetNumVertices();
        pr.Interpolate(l, src, dst); src = dst;
    }
    if (nLocal) pt->GetLocalPointStencilTable<Real>()->UpdateValues(&verts[0], &verts[nRefinerVertices]);
    Far::PatchMap patchmap(*pt);
    Far::PtexIndices ptex(*refiner);
    printf("ptexfaces %d\n", ptex.GetNumFaces());
    Real sv[] = { 0.0, 0.17, 0.5, 0.83, 1.0 };
    Real pW[20], dsW[20], dtW[20];
    for (int face = 0; face < ptex.GetNumFaces(); ++face) {
        for (int i = 0; i < 5; ++i) for (int j = 0; j < 5; ++j) {
            Real s = sv[i], t = sv[j];
            Far::PatchTable::PatchHandle const * h = patchmap.FindPatch(face, s, t);
            if (!h) { printf("nopatch %d %.2f %.2f\n", face, s, t); continue; }
            int htype = (int)pt->GetPatchArrayDescriptor(h->arrayIndex).GetType();
            if (htype == 6 || htype == 7) { printf("noeval %d %.2f %.2f patch %d\n", face, s, t, h->patchIndex); continue; }  // legacy Gregory has no basis evaluation
            pt->EvaluateBasis(*h, s, t, pW, dsW, dtW);
            Far::ConstIndexArray cvs = pt->GetPatchVertices(*h);
            Real P[3] = {0,0,0}, D1[3] = {0,0,0}, D2[3] = {0,0,0};
            for (int c = 0; c < cvs.size(); ++c) for (int k = 0; k < 3; ++k) {
                P[k] += pW[c] * verts[cvs[c]].p[k]; D1[k] += dsW[c] * verts[cvs[c]].p[k]; D2[k] += dtW[c] * verts[cvs[c]].p[k];
            }
            printf("eval %d %.2f %.2f patch %d P %.9g %.9g %.9g d1 %.9g %.9g %.9g d2 %.9g %.9g %.9g\n", face, s, t, h->patchIndex, P[0], P[1], P[2],
                   D1[0], D1[1], D1[2], D2[0], D2[1], D2[2]);
        }
    }
    return 0;
}
