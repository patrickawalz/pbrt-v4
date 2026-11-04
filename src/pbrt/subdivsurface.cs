// (not in pbrt-v4) subdivsurface.cs
//
// Catmull-Clark subdivision surfaces ray traced directly with Bezier clipping, after
//   Tejima, Fujita, Matsuoka: "Direct Ray Tracing of Full-Featured Subdivision Surfaces with Bezier Clipping", JCGT 2015.
// Feature adaptive subdivision (the OpenSubdiv port in src/ext/opensubdiv) isolates the extraordinary vertices and the creases; the regular B-spline
// patches are converted to Bezier patches, the Gregory patches at extraordinary vertices are replaced by Bezier patches with the averaged inner
// control points for the intersection. The exact limit surface (B-spline and Gregory bases) is evaluated at the hit.

using System;
using System.Collections.Generic;
using OpenSubdiv.Far;
using OpenSubdiv.Sdc;

namespace pbrt
{
    // The topology and the options of a subdivision surface
    public sealed class SubdivisionSurfaceInput
    {
        public double[] positions;
        public int[] faceVertexCounts;
        public int[] indices;
        public int[] creaseVertexPairs;
        public float[] creaseWeights;
        public int[] cornerVertices;
        public float[] cornerWeights;
        public int[] holes;
        public int isolationLevel = 3;
        public OpenSubdiv.Sdc.Options schemeOptions = new OpenSubdiv.Sdc.Options();
    }

    // The patch table and the control points of a subdivision surface
    public sealed class SubdivisionPatches
    {
        public PatchTable table;
        public double[] points;
        public int patchCount;
    }

    // The limit surface of one patch of a patch table
    public sealed class SubdivisionPatchSurface : PatchSurface
    {
        public SubdivisionPatchSurface(SubdivisionPatches patches, int arrayIndex, int patchInArray)
        {
            this.patches = patches;
            PatchTable table = patches.table;
            handle = new PatchTable.PatchHandle();
            handle.arrayIndex = arrayIndex;
            PatchTable.PatchArray array = table.getPatchArray(arrayIndex);
            handle.patchIndex = array.patchIndex + patchInArray;
            handle.vertIndex = array.vertIndex + patchInArray * array.desc.GetNumControlVertices();
            param = table.GetPatchParam(arrayIndex, patchInArray);
            OpenSubdiv.Vtr.ConstArray<int> vertices = table.GetPatchVertices(arrayIndex, patchInArray);
            gregory = table.GetPatchArrayDescriptor(arrayIndex).GetType() == PatchDescriptor.Type.GREGORY_BASIS;
            controlVertices = new int[vertices.size()];
            for (int i = 0; i < controlVertices.Length; ++i)
            {
                controlVertices[i] = vertices[i];
            }
        }

        public override void Evaluate(double u, double v, out SdfVector p, out SdfVector dpdu, out SdfVector dpdv, out Point2f uv, out SdfVector shadingNormal)
        {
            double s = u, t = v;
            param.Unnormalize(ref s, ref t);
            Weights w = Workspace();
            patches.table.EvaluateBasis(handle, s, t, w.p, w.ds, w.dt);
            double px = 0, py = 0, pz = 0, ux = 0, uy = 0, uz = 0, vx = 0, vy = 0, vz = 0;
            double[] points = patches.points;
            for (int c = 0; c < controlVertices.Length; ++c)
            {
                int k = 3 * controlVertices[c];
                double x = points[k], y = points[k + 1], z = points[k + 2];
                px += w.p[c] * x;
                py += w.p[c] * y;
                pz += w.p[c] * z;
                ux += w.ds[c] * x;
                uy += w.ds[c] * y;
                uz += w.ds[c] * z;
                vx += w.dt[c] * x;
                vy += w.dt[c] * y;
                vz += w.dt[c] * z;
            }
            p = new SdfVector(px, py, pz);
            dpdu = new SdfVector(ux, uy, uz);
            dpdv = new SdfVector(vx, vy, vz);
            if (gregory)
            {
                // The derivative weights of the Gregory basis of OpenSubdiv do not differentiate the rational blend of the inner points (the
                // true derivatives are disabled there), which is wrong near the extraordinary vertex: differentiate the positions instead
                double h = 1e-4;
                double ua = Math.Max(0, u - h), ub = Math.Min(1, u + h), va = Math.Max(0, v - h), vb = Math.Min(1, v + h);
                double sa = ua, ta = v, sb = ub, tb = v;
                param.Unnormalize(ref sa, ref ta);
                param.Unnormalize(ref sb, ref tb);
                double sc = u, tc = va, sd = u, td = vb;
                param.Unnormalize(ref sc, ref tc);
                param.Unnormalize(ref sd, ref td);
                dpdu = (PositionAt(ub, v) - PositionAt(ua, v)) / (sb - sa);
                dpdv = (PositionAt(u, vb) - PositionAt(u, va)) / (td - tc);
            }
            uv = new Point2f((Float)s, (Float)t);
            shadingNormal = new SdfVector(0, 0, 0);
        }

        public override int FaceIndex()
        {
            return param.GetFaceId();
        }

        private SdfVector PositionAt(double u, double v)
        {
            double s = u, t = v;
            param.Unnormalize(ref s, ref t);
            Weights w = Workspace();
            patches.table.EvaluateBasis(handle, s, t, w.p, w.ds, w.dt);
            double px = 0, py = 0, pz = 0;
            double[] points = patches.points;
            for (int c = 0; c < controlVertices.Length; ++c)
            {
                int k = 3 * controlVertices[c];
                px += w.p[c] * points[k];
                py += w.p[c] * points[k + 1];
                pz += w.p[c] * points[k + 2];
            }
            return new SdfVector(px, py, pz);
        }

        private sealed class Weights
        {
            public readonly double[] p = new double[20];
            public readonly double[] ds = new double[20];
            public readonly double[] dt = new double[20];
        }

        [ThreadStatic]
        private static Weights workspace;

        private static Weights Workspace()
        {
            if (workspace == null)
            {
                workspace = new Weights();
            }
            return workspace;
        }

        private readonly SubdivisionPatches patches;
        private readonly PatchTable.PatchHandle handle;
        private readonly PatchParam param;
        private readonly int[] controlVertices;
        private readonly bool gregory;
    }

    // A point of the primvar refinement
    public struct SubdivisionPoint : IPrimvar<SubdivisionPoint, double>
    {
        public double x;
        public double y;
        public double z;

        public void Clear()
        {
            x = 0;
            y = 0;
            z = 0;
        }

        public void AddWithWeight(SubdivisionPoint source, double weight)
        {
            x += weight * source.x;
            y += weight * source.y;
            z += weight * source.z;
        }
    }

    public static class SubdivisionSurface
    {
        // Refines adaptively and builds the patch table and the control points
        public static SubdivisionPatches BuildPatches(SubdivisionSurfaceInput input)
        {
            TopologyDescriptor descriptor = new TopologyDescriptor();
            descriptor.numVertices = input.positions.Length / 3;
            descriptor.numFaces = input.faceVertexCounts.Length;
            descriptor.numVertsPerFace = input.faceVertexCounts;
            descriptor.vertIndicesPerFace = input.indices;
            if (input.creaseWeights != null && input.creaseWeights.Length != 0)
            {
                descriptor.numCreases = input.creaseWeights.Length;
                descriptor.creaseVertexIndexPairs = input.creaseVertexPairs;
                descriptor.creaseWeights = input.creaseWeights;
            }
            if (input.cornerWeights != null && input.cornerWeights.Length != 0)
            {
                descriptor.numCorners = input.cornerWeights.Length;
                descriptor.cornerVertexIndices = input.cornerVertices;
                descriptor.cornerWeights = input.cornerWeights;
            }
            if (input.holes != null && input.holes.Length != 0)
            {
                descriptor.numHoles = input.holes.Length;
                descriptor.holeIndices = input.holes;
            }
            TopologyRefiner refiner = TopologyRefinerFactory<TopologyDescriptor>.Create(descriptor,
                new TopologyRefinerFactory<TopologyDescriptor>.Options(SchemeType.SCHEME_CATMARK, input.schemeOptions));

            PatchTableFactory.Options options = new PatchTableFactory.Options((uint)input.isolationLevel);
            options.SetPatchPrecision<double>();
            options.useInfSharpPatch = false;
            options.useSingleCreasePatch = false;
            options.generateVaryingTables = false;
            options.generateVaryingLocalPoints = false;
            options.SetEndCapType(PatchTableFactory.Options.EndCapType.ENDCAP_GREGORY_BASIS);
            refiner.RefineAdaptive(options.GetRefineAdaptiveOptions());
            PatchTable table = PatchTableFactory.Create(refiner, options);

            int refinerVertices = refiner.GetNumVerticesTotal();
            int localPoints = table.GetNumLocalPoints();
            SubdivisionPoint[] points = new SubdivisionPoint[refinerVertices + localPoints];
            for (int i = 0; i < descriptor.numVertices; ++i)
            {
                points[i].x = input.positions[3 * i];
                points[i].y = input.positions[3 * i + 1];
                points[i].z = input.positions[3 * i + 2];
            }
            PrimvarRefinerReal<double> primvarRefiner = new PrimvarRefinerReal<double>(refiner);
            int source = 0;
            for (int level = 1; level < refiner.GetNumLevels(); ++level)
            {
                int destination = source + refiner.GetLevel(level - 1).GetNumVertices();
                primvarRefiner.Interpolate<SubdivisionPoint, SubdivisionPoint>(level, new PrimvarBuffer<SubdivisionPoint>(points, source),
                                                                               new PrimvarBuffer<SubdivisionPoint>(points, destination));
                source = destination;
            }
            if (localPoints != 0)
            {
                table.GetLocalPointStencilTable<double>().UpdateValues<SubdivisionPoint, SubdivisionPoint>(
                    new PrimvarBuffer<SubdivisionPoint>(points, 0), new PrimvarBuffer<SubdivisionPoint>(points, refinerVertices));
            }
            SubdivisionPatches result = new SubdivisionPatches();
            result.table = table;
            result.points = new double[3 * points.Length];
            for (int i = 0; i < points.Length; ++i)
            {
                result.points[3 * i] = points[i].x;
                result.points[3 * i + 1] = points[i].y;
                result.points[3 * i + 2] = points[i].z;
            }
            result.patchCount = table.GetNumPatchesTotal();
            return result;
        }

        // The Bezier patch that bounds a patch: the conversion of the regular B-spline patches, and the averaged Gregory patches
        public static bool ToBezier(SubdivisionPatches patches, int arrayIndex, int patchInArray, out BezierPatch4 bezier)
        {
            bezier = default;
            PatchTable table = patches.table;
            PatchDescriptor.Type type = table.GetPatchArrayDescriptor(arrayIndex).GetType();
            OpenSubdiv.Vtr.ConstArray<int> vertices = table.GetPatchVertices(arrayIndex, patchInArray);
            double[] points = patches.points;
            if (type == PatchDescriptor.Type.REGULAR)
            {
                SdfVector[] P = new SdfVector[16];
                for (int j = 0; j < 16; ++j)
                {
                    int k = 3 * vertices[j];
                    P[j] = new SdfVector(points[k], points[k + 1], points[k + 2]);
                }
                SdfVector[] BP = BSplineToBezier(P);
                for (int k = 0; k < 16; ++k)
                {
                    bezier.Set(k / 4, k % 4, BP[k]);
                }
                return true;
            }
            if (type == PatchDescriptor.Type.GREGORY_BASIS)
            {
                SdfVector[] G = new SdfVector[20];
                for (int j = 0; j < 20; ++j)
                {
                    int k = 3 * vertices[j];
                    G[j] = new SdfVector(points[k], points[k + 1], points[k + 2]);
                }
                bezier = GregoryToBezier(G);
                return true;
            }
            return false;
        }

        // The Bezier patch of the 20 control points of a Gregory patch: the corners and the boundary points, the inner points are the averages of the
        // pairs of each corner (corner c has P, Ep, Em, Fp, Fm at 5c ... 5c + 4)
        public static BezierPatch4 GregoryToBezier(SdfVector[] G)
        {
            BezierPatch4 bezier = default;
            bezier.Set(0, 0, G[0]);
            bezier.Set(1, 0, G[1]);
            bezier.Set(2, 0, G[7]);
            bezier.Set(3, 0, G[5]);
            bezier.Set(3, 1, G[6]);
            bezier.Set(3, 2, G[12]);
            bezier.Set(3, 3, G[10]);
            bezier.Set(2, 3, G[11]);
            bezier.Set(1, 3, G[17]);
            bezier.Set(0, 3, G[15]);
            bezier.Set(0, 2, G[16]);
            bezier.Set(0, 1, G[2]);
            bezier.Set(1, 1, (G[3] + G[4]) * 0.5);
            bezier.Set(2, 1, (G[8] + G[9]) * 0.5);
            bezier.Set(2, 2, (G[13] + G[14]) * 0.5);
            bezier.Set(1, 2, (G[18] + G[19]) * 0.5);
            return bezier;
        }

        // The conversion of the 16 control points of a uniform cubic B-spline patch to Bezier control points with the watertight evaluation order of
        // convert_bezier.cpp in the code of Tejima et al.
        public static SdfVector[] BSplineToBezier(SdfVector[] P)
        {
            SdfVector[] BP = new SdfVector[16];
            BP[0] = (((P[0] + P[10]) + (P[2] + P[8])) + (4 * (P[4] + P[6]) + 4 * (P[1] + P[9]))) + 16 * P[5];
            BP[1] = (4 * (P[4] + P[6]) + 2 * (P[8] + P[10])) + 4 * (4 * P[5] + 2 * P[9]);
            BP[2] = (4 * (P[8] + P[10]) + 2 * (P[4] + P[6])) + 4 * (4 * P[9] + 2 * P[5]);
            BP[3] = (((P[12] + P[6]) + (P[4] + P[14])) + (4 * (P[8] + P[10]) + 4 * (P[5] + P[13]))) + 16 * P[9];
            BP[4] = (4 * (P[1] + P[9]) + 2 * (P[2] + P[10])) + 4 * (4 * P[5] + 2 * P[6]);
            BP[5] = (4 * (4 * P[5] + 2 * P[6]) + 2 * (2 * P[10] + 4 * P[9]));
            BP[6] = (2 * (4 * P[5] + 2 * P[6]) + 4 * (2 * P[10] + 4 * P[9]));
            BP[7] = (4 * (P[5] + P[13]) + 2 * (P[6] + P[14])) + 4 * (4 * P[9] + 2 * P[10]);
            BP[8] = (4 * (P[2] + P[10]) + 2 * (P[1] + P[9])) + 4 * (2 * P[5] + 4 * P[6]);
            BP[9] = (4 * (2 * P[5] + 4 * P[6]) + 2 * (4 * P[10] + 2 * P[9]));
            BP[10] = (2 * (2 * P[5] + 4 * P[6]) + 4 * (4 * P[10] + 2 * P[9]));
            BP[11] = (4 * (P[6] + P[14]) + 2 * (P[5] + P[13])) + 4 * (4 * P[10] + 2 * P[9]);
            BP[12] = (((P[3] + P[9]) + (P[1] + P[11])) + (4 * (P[2] + P[10]) + 4 * (P[5] + P[7]))) + 16 * P[6];
            BP[13] = (4 * (P[5] + P[7]) + 2 * (P[9] + P[11])) + 4 * (4 * P[6] + 2 * P[10]);
            BP[14] = (4 * (P[9] + P[11]) + 2 * (P[5] + P[7])) + 4 * (4 * P[10] + 2 * P[6]);
            BP[15] = (((P[5] + P[15]) + (P[7] + P[13])) + (4 * (P[6] + P[14]) + 4 * (P[9] + P[11]))) + 16 * P[10];
            for (int j = 0; j < 16; ++j)
            {
                BP[j] = BP[j] * (1.0 / 36.0);
            }
            return BP;
        }

        // The shapes of the patches
        public static pstd.vector<Shape> CreateShapes(BezierSurfaceSettings settings, SubdivisionPatches patches)
        {
            pstd.vector<Shape> shapes = new pstd.vector<Shape>();
            PatchTable table = patches.table;
            for (int a = 0; a < table.GetNumPatchArrays(); ++a)
            {
                for (int p = 0; p < table.GetNumPatches(a); ++p)
                {
                    if (!ToBezier(patches, a, p, out BezierPatch4 bezier))
                    {
                        continue;
                    }
                    bool transition = table.GetPatchParam(a, p).GetTransition() != 0;
                    shapes.push_back(new BezierClipPatchShape(settings, bezier, new SubdivisionPatchSurface(patches, a, p), transition));
                }
            }
            return shapes;
        }

        // The shapes of a "catmarksubdiv" shape
        public static pstd.vector<Shape> Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation,
                                                ParameterDictionary parameters, FileLoc loc)
        {
            SubdivisionSurfaceInput input = new SubdivisionSurfaceInput();
            List<Point3f> points = parameters.GetPoint3fArray("P");
            List<int> indices = parameters.GetIntArray("indices");
            string filename = parameters.GetOneString("filename", "");
            List<int> counts = parameters.GetIntArray("faceVertexCounts");
            if (filename.Length != 0)
            {
                TriQuadMesh ply = TriQuadMesh.ReadPLY(pbrt.ResolveFilename(filename));
                points = new List<Point3f>();
                for (int i = 0; i < (int)ply.p.size(); ++i)
                {
                    points.Add(ply.p[i]);
                }
                indices = new List<int>();
                counts = new List<int>();
                for (int i = 0; i + 2 < (int)ply.triIndices.size(); i += 3)
                {
                    indices.Add(ply.triIndices[i]);
                    indices.Add(ply.triIndices[i + 1]);
                    indices.Add(ply.triIndices[i + 2]);
                    counts.Add(3);
                }
                for (int i = 0; i + 3 < (int)ply.quadIndices.size(); i += 4)
                {
                    // The quads of a TriQuadMesh are in the order of a bilinear patch
                    indices.Add(ply.quadIndices[i]);
                    indices.Add(ply.quadIndices[i + 1]);
                    indices.Add(ply.quadIndices[i + 3]);
                    indices.Add(ply.quadIndices[i + 2]);
                    counts.Add(4);
                }
            }
            if (points.Count == 0 || indices.Count == 0)
            {
                pbrt.ErrorExit(loc, "catmarksubdiv: \"P\" and \"indices\" (or a \"filename\") are required.");
            }
            if (counts.Count == 0)
            {
                int degree = indices.Count % 4 == 0 ? 4 : 3;
                if (indices.Count % degree != 0)
                {
                    pbrt.ErrorExit(loc, "catmarksubdiv: without \"faceVertexCounts\" the faces must be all quads or all triangles.");
                }
                for (int i = 0; i < indices.Count / degree; ++i)
                {
                    counts.Add(degree);
                }
            }
            input.positions = new double[3 * points.Count];
            double[] low = { double.MaxValue, double.MaxValue, double.MaxValue }, high = { double.MinValue, double.MinValue, double.MinValue };
            for (int i = 0; i < points.Count; ++i)
            {
                input.positions[3 * i] = points[i].x;
                input.positions[3 * i + 1] = points[i].y;
                input.positions[3 * i + 2] = points[i].z;
                for (int c = 0; c < 3; ++c)
                {
                    low[c] = Math.Min(low[c], input.positions[3 * i + c]);
                    high[c] = Math.Max(high[c], input.positions[3 * i + c]);
                }
            }
            double diagonal = new SdfVector(high[0] - low[0], high[1] - low[1], high[2] - low[2]).Length();
            input.faceVertexCounts = counts.ToArray();
            input.indices = indices.ToArray();
            List<int> creaseIndices = parameters.GetIntArray("creaseindices");
            List<float> creaseWeights = parameters.GetFloatArray("creaseweights");
            if (creaseWeights.Count != 0)
            {
                input.creaseVertexPairs = creaseIndices.ToArray();
                input.creaseWeights = creaseWeights.ToArray();
            }
            List<int> cornerIndices = parameters.GetIntArray("cornerindices");
            List<float> cornerWeights = parameters.GetFloatArray("cornerweights");
            if (cornerWeights.Count != 0)
            {
                input.cornerVertices = cornerIndices.ToArray();
                input.cornerWeights = cornerWeights.ToArray();
            }
            input.holes = parameters.GetIntArray("holes").ToArray();
            input.isolationLevel = Math.Max(1, Math.Min(10, parameters.GetOneInt("levels", 3)));
            string boundary = parameters.GetOneString("boundary", "edgeonly");
            input.schemeOptions.SetVtxBoundaryInterpolation(boundary == "none" ? OpenSubdiv.Sdc.Options.VtxBoundaryInterpolation.VTX_BOUNDARY_NONE :
                boundary == "edgeandcorner" ? OpenSubdiv.Sdc.Options.VtxBoundaryInterpolation.VTX_BOUNDARY_EDGE_AND_CORNER :
                OpenSubdiv.Sdc.Options.VtxBoundaryInterpolation.VTX_BOUNDARY_EDGE_ONLY);
            input.schemeOptions.SetCreasingMethod(parameters.GetOneString("creasing", "uniform") == "chaikin" ?
                OpenSubdiv.Sdc.Options.CreasingMethod.CREASE_CHAIKIN : OpenSubdiv.Sdc.Options.CreasingMethod.CREASE_UNIFORM);
            input.schemeOptions.SetTriangleSubdivision(parameters.GetOneString("trisub", "catmark") == "smooth" ?
                OpenSubdiv.Sdc.Options.TriangleSubdivision.TRI_SUB_SMOOTH : OpenSubdiv.Sdc.Options.TriangleSubdivision.TRI_SUB_CATMARK);

            BezierSurfaceSettings settings = new BezierSurfaceSettings();
            settings.renderFromObject = renderFromObject;
            settings.objectFromRender = objectFromRender;
            settings.reverseOrientation = reverseOrientation;
            settings.transformSwapsHandedness = renderFromObject.SwapsHandedness();
            settings.epsilon = parameters.GetOneFloat("epsilon", 1e-5f) * diagonal;
            settings.pixelSpread = parameters.GetOneFloat("pixelspread", 0);
            settings.clip.maxLevel = parameters.GetOneInt("maxlevel", 10);
            settings.autoMaxLevel = parameters.GetOneInt("maxlevel", -1) < 0;
            settings.clip.uvMargin = parameters.GetOneFloat("uvmargin", 1);
            settings.clip.useBezierClip = parameters.GetOneBool("bezierclip", true);
            settings.clip.conservative = parameters.GetOneBool("conservative", false);
            pstd.vector<Shape> shapes = CreateShapes(settings, BuildPatches(input));
            PatchDisplacement displacement = PatchDisplacement.Create(parameters, loc);
            return displacement != null ? displacement.Apply(shapes, settings) : shapes;
        }
    }
}
