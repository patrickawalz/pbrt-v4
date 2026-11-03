// osdtest.cs: runs the C# OpenSubdiv port on the test meshes and compares the dump with the native oracle output
// (expected/*.txt, produced by oracle/osd_oracle.cpp). Usage: osdtest <testsdir> [name-filter] [--write outdir]
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using OpenSubdiv.Far;
using OpenSubdiv.Sdc;
using OpenSubdiv.Vtr;

namespace OpenSubdiv
{
    public struct Vertex : IPrimvar<Vertex, double>
    {
        public double p0;
        public double p1;
        public double p2;

        public void Clear()
        {
            p0 = 0.0;
            p1 = 0.0;
            p2 = 0.0;
        }

        public void AddWithWeight(Vertex s, double w)
        {
            p0 += w * s.p0;
            p1 += w * s.p1;
            p2 += w * s.p2;
        }
    }

    public class Mesh
    {
        public string scheme = "catmark";
        public Dictionary<string, int> opt = new Dictionary<string, int>();
        public List<double> verts = new List<double>();
        public List<int> vertsPerFace = new List<int>();
        public List<int> faceVerts = new List<int>();
        public List<int> creaseVerts = new List<int>();
        public List<float> creaseWeights = new List<float>();
        public List<int> cornerVerts = new List<int>();
        public List<float> cornerWeights = new List<float>();
        public List<int> holes = new List<int>();

        public int Get(string key, int def)
        {
            int v;
            return opt.TryGetValue(key, out v) ? v : def;
        }
    }

    public static class OsdTest
    {
        static CultureInfo inv = CultureInfo.InvariantCulture;

        static string G9(double v)
        {
            return v.ToString("G9", inv);
        }

        static Mesh ParseMesh(string path)
        {
            Mesh m = new Mesh();
            foreach (string line in File.ReadAllLines(path))
            {
                string[] t = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (t.Length == 0 || t[0][0] == '#')
                {
                    continue;
                }
                switch (t[0])
                {
                    case "scheme":
                        m.scheme = t[1];
                        break;
                    case "opt":
                        m.opt[t[1]] = int.Parse(t[2], inv);
                        break;
                    case "v":
                        m.verts.Add(double.Parse(t[1], inv));
                        m.verts.Add(double.Parse(t[2], inv));
                        m.verts.Add(double.Parse(t[3], inv));
                        break;
                    case "f":
                        for (int i = 1; i < t.Length; ++i)
                        {
                            m.faceVerts.Add(int.Parse(t[i], inv));
                        }
                        m.vertsPerFace.Add(t.Length - 1);
                        break;
                    case "crease":
                        m.creaseVerts.Add(int.Parse(t[1], inv));
                        m.creaseVerts.Add(int.Parse(t[2], inv));
                        m.creaseWeights.Add(float.Parse(t[3], inv));
                        break;
                    case "corner":
                        m.cornerVerts.Add(int.Parse(t[1], inv));
                        m.cornerWeights.Add(float.Parse(t[2], inv));
                        break;
                    case "hole":
                        m.holes.Add(int.Parse(t[1], inv));
                        break;
                }
            }
            return m;
        }

        static void DumpIndices(StringBuilder o, string tag, ConstArray<int> a)
        {
            o.Append(tag);
            for (int i = 0; i < a.size(); ++i)
            {
                o.Append(' ').Append(a[i]);
            }
            o.Append('\n');
        }

        static void DumpLocalIndices(StringBuilder o, string tag, ConstArray<ushort> a)
        {
            o.Append(tag);
            for (int i = 0; i < a.size(); ++i)
            {
                o.Append(' ').Append((int)a[i]);
            }
            o.Append('\n');
        }

        static int B(bool b)
        {
            return b ? 1 : 0;
        }

        static void DumpLevel(StringBuilder o, TopologyLevel L, int level)
        {
            o.Append("level " + level + " verts " + L.GetNumVertices() + " edges " + L.GetNumEdges() + " faces " + L.GetNumFaces() +
                     " faceverts " + L.GetNumFaceVertices() + "\n");
            for (int f = 0; f < L.GetNumFaces(); ++f)
            {
                DumpIndices(o, "fv", L.GetFaceVertices(f));
                DumpIndices(o, "fe", L.GetFaceEdges(f));
                o.Append("fhole " + B(L.IsFaceHole(f)) + "\n");
            }
            for (int e = 0; e < L.GetNumEdges(); ++e)
            {
                DumpIndices(o, "ev", L.GetEdgeVertices(e));
                DumpIndices(o, "ef", L.GetEdgeFaces(e));
                o.Append("esharp " + G9(L.GetEdgeSharpness(e)) + " nonmanifold " + B(L.IsEdgeNonManifold(e)) + " boundary " + B(L.IsEdgeBoundary(e)) +
                         " infsharp " + B(L.IsEdgeInfSharp(e)) + " semisharp " + B(L.IsEdgeSemiSharp(e)) + "\n");
            }
            for (int v = 0; v < L.GetNumVertices(); ++v)
            {
                DumpIndices(o, "ve", L.GetVertexEdges(v));
                DumpIndices(o, "vf", L.GetVertexFaces(v));
                DumpLocalIndices(o, "vfl", L.GetVertexFaceLocalIndices(v));
                o.Append("vsharp " + G9(L.GetVertexSharpness(v)) + " nonmanifold " + B(L.IsVertexNonManifold(v)) + " boundary " + B(L.IsVertexBoundary(v)) +
                         " corner " + B(L.IsVertexCorner(v)) + " regular " + B(L.IsVertexValenceRegular(v)) + " infsharp " + B(L.IsVertexInfSharp(v)) +
                         " semisharp " + B(L.IsVertexSemiSharp(v)) + " rule " + (int)L.GetVertexRule(v) + "\n");
            }
        }

        static void DumpStencils(StringBuilder o, string tag, StencilTableReal<double> t)
        {
            if (t == null)
            {
                o.Append(tag + " none\n");
                return;
            }
            o.Append(tag + " stencils " + t.GetNumStencils() + " elements " + t.GetControlIndices().Count + "\n");
            for (int i = 0; i < t.GetNumStencils(); ++i)
            {
                StencilReal<double> s = t.GetStencil(i);
                o.Append("st " + s.GetSize() + ":");
                ConstArray<int> idx = s.GetVertexIndices();
                ConstArray<double> w = s.GetWeights();
                for (int j = 0; j < s.GetSize(); ++j)
                {
                    o.Append(' ').Append(idx[j]).Append(' ').Append(G9(w[j]));
                }
                o.Append('\n');
            }
        }

        static string P3(Vertex v)
        {
            return G9(v.p0) + " " + G9(v.p1) + " " + G9(v.p2);
        }

        public static string Dump(string meshPath)
        {
            Mesh m = ParseMesh(meshPath);
            StringBuilder o = new StringBuilder();
            SchemeType scheme = m.scheme == "loop" ? SchemeType.SCHEME_LOOP : m.scheme == "bilinear" ? SchemeType.SCHEME_BILINEAR : SchemeType.SCHEME_CATMARK;
            Sdc.Options sdcOptions = new Sdc.Options();
            sdcOptions.SetVtxBoundaryInterpolation((Sdc.Options.VtxBoundaryInterpolation)m.Get("boundary", 1));
            sdcOptions.SetFVarLinearInterpolation((Sdc.Options.FVarLinearInterpolation)m.Get("fvarlinear", 1));
            sdcOptions.SetCreasingMethod((Sdc.Options.CreasingMethod)m.Get("creasing", 0));
            sdcOptions.SetTriangleSubdivision((Sdc.Options.TriangleSubdivision)m.Get("trisub", 0));

            TopologyDescriptor desc = new TopologyDescriptor();
            desc.numVertices = m.verts.Count / 3;
            desc.numFaces = m.vertsPerFace.Count;
            desc.numVertsPerFace = m.vertsPerFace.ToArray();
            desc.vertIndicesPerFace = m.faceVerts.ToArray();
            desc.numCreases = m.creaseWeights.Count;
            if (desc.numCreases != 0)
            {
                desc.creaseVertexIndexPairs = m.creaseVerts.ToArray();
                desc.creaseWeights = m.creaseWeights.ToArray();
            }
            desc.numCorners = m.cornerWeights.Count;
            if (desc.numCorners != 0)
            {
                desc.cornerVertexIndices = m.cornerVerts.ToArray();
                desc.cornerWeights = m.cornerWeights.ToArray();
            }
            desc.numHoles = m.holes.Count;
            if (desc.numHoles != 0)
            {
                desc.holeIndices = m.holes.ToArray();
            }

            TopologyRefiner refiner = TopologyRefinerFactory<TopologyDescriptor>.Create(desc,
                new TopologyRefinerFactory<TopologyDescriptor>.Options(scheme, sdcOptions));
            o.Append("scheme " + m.scheme + "\n");

            int uniform = m.Get("uniform", 0);
            if (uniform != 0)
            {
                TopologyRefiner.UniformOptions uo = new TopologyRefiner.UniformOptions(uniform);
                uo.fullTopologyInLastLevel = m.Get("fulltopo", 1) != 0;
                refiner.RefineUniform(uo);
                o.Append("uniform " + uniform + "\n");
                for (int l = 0; l < refiner.GetNumLevels(); ++l)
                {
                    DumpLevel(o, refiner.GetLevel(l), l);
                }
                Vertex[] verts = new Vertex[refiner.GetNumVerticesTotal()];
                for (int i = 0; i < m.verts.Count / 3; ++i)
                {
                    verts[i].p0 = m.verts[3 * i];
                    verts[i].p1 = m.verts[3 * i + 1];
                    verts[i].p2 = m.verts[3 * i + 2];
                }
                PrimvarRefinerReal<double> pr = new PrimvarRefinerReal<double>(refiner);
                int srcOffset = 0;
                for (int l = 1; l < refiner.GetNumLevels(); ++l)
                {
                    int dstOffset = srcOffset + refiner.GetLevel(l - 1).GetNumVertices();
                    pr.Interpolate<Vertex, Vertex>(l, new PrimvarBuffer<Vertex>(verts, srcOffset), new PrimvarBuffer<Vertex>(verts, dstOffset));
                    srcOffset = dstOffset;
                }
                for (int i = 0; i < verts.Length; ++i)
                {
                    o.Append("pt " + P3(verts[i]) + "\n");
                }
                int n = refiner.GetLevel(refiner.GetNumLevels() - 1).GetNumVertices();
                Vertex[] lp = new Vertex[n];
                Vertex[] l1 = new Vertex[n];
                Vertex[] l2 = new Vertex[n];
                pr.Limit<Vertex, Vertex, Vertex, Vertex>(new PrimvarBuffer<Vertex>(verts, srcOffset), new PrimvarBuffer<Vertex>(lp, 0),
                    new PrimvarBuffer<Vertex>(l1, 0), new PrimvarBuffer<Vertex>(l2, 0));
                for (int i = 0; i < n; ++i)
                {
                    o.Append("limit " + P3(lp[i]) + " t1 " + P3(l1[i]) + " t2 " + P3(l2[i]) + "\n");
                }
                return o.ToString();
            }

            int level = m.Get("level", 3);
            PatchTableFactory.Options po = new PatchTableFactory.Options((uint)level);
            po.SetPatchPrecision<double>();
            po.useInfSharpPatch = m.Get("infsharp", 0) != 0;
            po.useSingleCreasePatch = m.Get("singlecrease", 0) != 0;
            po.generateVaryingTables = false;
            po.generateVaryingLocalPoints = false;
            po.SetEndCapType((PatchTableFactory.Options.EndCapType)m.Get("endcap", 3));
            po.shareEndCapPatchPoints = m.Get("share", 1) != 0;
            po.generateLegacySharpCornerPatches = m.Get("legacycorner", 1) != 0;
            refiner.RefineAdaptive(po.GetRefineAdaptiveOptions());
            o.Append("adaptive " + level + " levels " + refiner.GetNumLevels() + "\n");
            for (int l = 0; l < refiner.GetNumLevels(); ++l)
            {
                DumpLevel(o, refiner.GetLevel(l), l);
            }

            PatchTable pt = PatchTableFactory.Create(refiner, po);
            o.Append("patchtable arrays " + pt.GetNumPatchArrays() + " patches " + pt.GetNumPatchesTotal() + " cvs " + pt.GetNumControlVerticesTotal() +
                     " localpoints " + pt.GetNumLocalPoints() + " maxvalence " + pt.GetMaxValence() + "\n");
            for (int a = 0; a < pt.GetNumPatchArrays(); ++a)
            {
                PatchDescriptor d = pt.GetPatchArrayDescriptor(a);
                o.Append("array " + a + " type " + (int)d.GetType() + " patches " + pt.GetNumPatches(a) + " cvs " + d.GetNumControlVertices() + "\n");
                for (int p = 0; p < pt.GetNumPatches(a); ++p)
                {
                    PatchParam pp = pt.GetPatchParam(a, p);
                    o.Append("param face " + pp.GetFaceId() + " u " + pp.GetU() + " v " + pp.GetV() + " depth " + pp.GetDepth() + " nonquad " +
                             B(pp.NonQuadRoot()) + " boundary " + pp.GetBoundary() + " trans " + pp.GetTransition() + " reg " + B(pp.IsRegular()) + "\n");
                    DumpIndices(o, "pcv", pt.GetPatchVertices(a, p));
                }
            }
            o.Append("quadoffsets " + pt.GetNumPatchesTotal() + "\n");
            DumpStencils(o, "localstencils", pt.GetLocalPointStencilTable<double>());

            int nRefinerVertices = refiner.GetNumVerticesTotal();
            int nLocal = pt.GetNumLocalPoints();
            Vertex[] pv = new Vertex[nRefinerVertices + nLocal];
            for (int i = 0; i < m.verts.Count / 3; ++i)
            {
                pv[i].p0 = m.verts[3 * i];
                pv[i].p1 = m.verts[3 * i + 1];
                pv[i].p2 = m.verts[3 * i + 2];
            }
            PrimvarRefinerReal<double> pr3 = new PrimvarRefinerReal<double>(refiner);
            int so = 0;
            for (int l = 1; l < refiner.GetNumLevels(); ++l)
            {
                int dstOffset = so + refiner.GetLevel(l - 1).GetNumVertices();
                pr3.Interpolate<Vertex, Vertex>(l, new PrimvarBuffer<Vertex>(pv, so), new PrimvarBuffer<Vertex>(pv, dstOffset));
                so = dstOffset;
            }
            if (nLocal != 0)
            {
                pt.GetLocalPointStencilTable<double>().UpdateValues<Vertex, Vertex>(new PrimvarBuffer<Vertex>(pv, 0), new PrimvarBuffer<Vertex>(pv, nRefinerVertices));
            }
            PatchMap patchmap = new PatchMap(pt);
            PtexIndices ptex = new PtexIndices(refiner);
            o.Append("ptexfaces " + ptex.GetNumFaces() + "\n");
            double[] sv = { 0.0, 0.17, 0.5, 0.83, 1.0 };
            double[] pW = new double[20];
            double[] dsW = new double[20];
            double[] dtW = new double[20];
            for (int face = 0; face < ptex.GetNumFaces(); ++face)
            {
                for (int i = 0; i < 5; ++i)
                {
                    for (int j = 0; j < 5; ++j)
                    {
                        double s = sv[i];
                        double t = sv[j];
                        PatchTable.PatchHandle h = patchmap.FindPatch(face, s, t);
                        string st = s.ToString("F2", inv) + " " + t.ToString("F2", inv);
                        if (h == null)
                        {
                            o.Append("nopatch " + face + " " + st + "\n");
                            continue;
                        }
                        int htype = (int)pt.GetPatchArrayDescriptor(h.arrayIndex).GetType();
                        if (htype == 6 || htype == 7)
                        {
                            o.Append("noeval " + face + " " + st + " patch " + h.patchIndex + "\n");
                            continue;
                        }
                        pt.EvaluateBasis(h, s, t, pW, dsW, dtW);
                        ConstArray<int> cvs = pt.GetPatchVertices(h);
                        Vertex P = new Vertex();
                        Vertex D1 = new Vertex();
                        Vertex D2 = new Vertex();
                        for (int c = 0; c < cvs.size(); ++c)
                        {
                            Vertex x = pv[cvs[c]];
                            P.p0 += pW[c] * x.p0; P.p1 += pW[c] * x.p1; P.p2 += pW[c] * x.p2;
                            D1.p0 += dsW[c] * x.p0; D1.p1 += dsW[c] * x.p1; D1.p2 += dsW[c] * x.p2;
                            D2.p0 += dtW[c] * x.p0; D2.p1 += dtW[c] * x.p1; D2.p2 += dtW[c] * x.p2;
                        }
                        o.Append("eval " + face + " " + st + " patch " + h.patchIndex + " P " + P3(P) + " d1 " + P3(D1) + " d2 " + P3(D2) + "\n");
                    }
                }
            }
            return o.ToString();
        }

        static bool TokensMatch(string a, string b)
        {
            if (a == b)
            {
                return true;
            }
            double x;
            double y;
            if (double.TryParse(a, NumberStyles.Float, inv, out x) && double.TryParse(b, NumberStyles.Float, inv, out y))
            {
                return Math.Abs(x - y) <= 2e-6 + 2e-5 * Math.Max(Math.Abs(x), Math.Abs(y));
            }
            return false;
        }

        static string Compare(string actual, string expected)
        {
            string[] a = actual.Split('\n');
            string[] e = expected.Split('\n');
            int n = Math.Min(a.Length, e.Length);
            for (int i = 0; i < n; ++i)
            {
                string[] ta = a[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string[] te = e[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                bool ok = ta.Length == te.Length;
                for (int k = 0; ok && k < ta.Length; ++k)
                {
                    ok = TokensMatch(ta[k], te[k]);
                }
                if (!ok)
                {
                    return "line " + (i + 1) + "\n  expected: " + e[i] + "\n  actual:   " + a[i];
                }
            }
            if (a.Length != e.Length)
            {
                return "line count " + a.Length + " vs expected " + e.Length;
            }
            return null;
        }

        public static int Main(string[] args)
        {
            string dir = args.Length > 0 ? args[0] : ".";
            string filter = args.Length > 1 ? args[1] : "";
            int failures = 0;
            int count = 0;
            string[] meshes = Directory.GetFiles(Path.Combine(dir, "meshes"), "*.txt");
            Array.Sort(meshes, StringComparer.Ordinal);
            foreach (string mesh in meshes)
            {
                string name = Path.GetFileNameWithoutExtension(mesh);
                if (filter.Length != 0 && !name.Contains(filter))
                {
                    continue;
                }
                ++count;
                string actual;
                try
                {
                    actual = Dump(mesh);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("FAIL " + name + ": exception " + ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace);
                    ++failures;
                    continue;
                }
                string expected = File.ReadAllText(Path.Combine(dir, "expected", name + ".txt"));
                string diff = Compare(actual, expected);
                if (diff != null)
                {
                    Console.WriteLine("FAIL " + name + ": " + diff);
                    ++failures;
                }
                else
                {
                    Console.WriteLine("PASS " + name);
                }
            }
            Console.WriteLine(count + " meshes, " + failures + " failures");
            return failures == 0 ? 0 : 1;
        }
    }
}
