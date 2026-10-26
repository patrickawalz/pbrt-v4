//
// cyhair2pbrt.cpp
//
// Convert CyHair files to PBRT.
// Hair vertices are interpreted as Catmull-Rom spline points.
// The tool simply converts Catmull-Rom spline points to cubic Bezier points.
//
// MIT license
//

///////////// start of cyhair_loader

/*
The MIT License (MIT)

Copyright (c) 2016 Light Transport Entertainment, Inc.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

// Simple Cyhair loader.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace cyhair
{
    public struct real3
    {
        public real3(float v) { x = v; y = v; z = v; }
        public real3(float xx, float yy, float zz) { x = xx; y = yy; z = zz; }

        public static real3 operator +(real3 a, real3 f2) { return new real3(a.x + f2.x, a.y + f2.y, a.z + f2.z); }
        public static real3 operator *(real3 a, real3 f2) { return new real3(a.x * f2.x, a.y * f2.y, a.z * f2.z); }
        public static real3 operator /(real3 a, real3 f2) { return new real3(a.x / f2.x, a.y / f2.y, a.z / f2.z); }
        public static real3 operator /(real3 a, float f) { return new real3(a.x / f, a.y / f, a.z / f); }
        public static real3 operator *(float f, real3 v) { return new real3(v.x * f, v.y * f, v.z * f); }
        public static real3 operator *(real3 v, float f) { return new real3(v.x * f, v.y * f, v.z * f); }

        public float x, y, z;
    }

    public unsafe class CyHair
    {
        static readonly float[,] toC2B = {
            {0.0f, 6.0f / 6.0f, 0.0f, 0.0f},
            {-1.0f / 6.0f, 6.0f / 6.0f, 1.0f / 6.0f, 0.0f},
            {0.0f, 1.0f / 6.0f, 6.0f / 6.0f, -1.0f / 6.0f},
            {0.0f, 0.0f, 6.0f / 6.0f, 0.0f}};

        static readonly float[,] toC2B0 = {
            {0.0f, 6.0f / 6.0f, 0.0f, 0.0f},
            {0.0f, 3.0f / 6.0f, 4.0f / 6.0f, -1.0f / 6.0f},
            {0.0f, 1.0f / 6.0f, 6.0f / 6.0f, -1.0f / 6.0f},
            {0.0f, 0.0f, 6.0f / 6.0f, 0.0f}};

        static readonly float[,] toC2B1 = {
            {0.0f, 6.0f / 6.0f, 0.0f, 0.0f},
            {-1.0f / 6.0f, 6.0f / 6.0f, 1.0f / 6.0f, 0.0f},
            {-1.0f / 6.0f, 4.0f / 6.0f, 3.0f / 6.0f, 0.0f},
            {0.0f, 0.0f, 6.0f / 6.0f, 0.0f}};

        static void mul_matrix(real3[] outp, float[,] mat, real3[] pt)
        {
            for (int i = 0; i < 4; i++)
            {
                outp[i] = mat[i, 0] * pt[0] + mat[i, 1] * pt[1] + mat[i, 2] * pt[2] +
                          mat[i, 3] * pt[3];
            }
        }

        static void CamullRomToCubicBezier(real3[] Q, real3[] cps, int cps_size, int seg_idx)
        {
            ulong sz = (ulong)cps_size;
            if (sz == 2)
            {
                Q[0] = cps[seg_idx];
                Q[1] = cps[seg_idx] * 2.0f / 3.0f + cps[seg_idx + 1] * 1.0f / 3.0f;
                Q[2] = cps[seg_idx] * 1.0f / 3.0f + cps[seg_idx + 1] * 2.0f / 3.0f;
                Q[3] = cps[seg_idx + 1];
            }
            else
            {
                real3[] P = new real3[4];
                if (seg_idx == 0)
                {
                    P[0] = new real3(0.0f);
                    P[1] = cps[seg_idx + 0];
                    P[2] = cps[seg_idx + 1];
                    P[3] = cps[seg_idx + 2];
                    mul_matrix(Q, toC2B0, P);
                }
                else if (seg_idx == (int)(sz - 2))
                {
                    P[0] = cps[seg_idx - 1];
                    P[1] = cps[seg_idx + 0];
                    P[2] = cps[seg_idx + 1];
                    P[3] = new real3(0.0f);
                    mul_matrix(Q, toC2B1, P);
                }
                else
                {
                    P[0] = cps[seg_idx - 1];
                    P[1] = cps[seg_idx + 0];
                    P[2] = cps[seg_idx + 1];
                    P[3] = cps[seg_idx + 2];
                    mul_matrix(Q, toC2B, P);
                }
            }
        }

        public CyHair()
        {
            flags_ = 0;
            num_strands_ = 0;
            total_points_ = 0;
            default_segments_ = -1;
            default_thickness_ = 0.01f;
            default_transparency_ = 1.0f;
            default_color_[0] = 0.5f;
            default_color_[1] = 0.5f;
            default_color_[2] = 0.5f;
        }

        // fread(dst, n, 1, fp) == 1 semantics: all n bytes must be read (and n must be non-zero).
        static bool ReadAll(Stream fp, byte[] dst, int n)
        {
            if (n == 0) return false;
            int got = 0;
            while (got < n)
            {
                int r = fp.Read(dst, got, n - got);
                if (r <= 0) return false;
                got += r;
            }
            return true;
        }

        /// Load CyHair data from a file.
        public bool Load(string filename)
        {
            FileStream fp;
            try { fp = new FileStream(filename, FileMode.Open, FileAccess.Read); }
            catch (Exception) { return false; }

            using (fp)
            {
                byte[] hdr = new byte[128];
                if (!ReadAll(fp, hdr, 128))
                    return false;
                if (!(hdr[0] == 'H' && hdr[1] == 'A' && hdr[2] == 'I' && hdr[3] == 'R'))
                    return false;

                // CyHairHeader layout: char magic[4]; uint num_strands, total_points, flags,
                // default_segments; float default_thickness, default_transparency;
                // float default_color[3]; char information[88];
                uint hdr_num_strands = BitConverter.ToUInt32(hdr, 4);
                uint hdr_total_points = BitConverter.ToUInt32(hdr, 8);
                uint hdr_flags = BitConverter.ToUInt32(hdr, 12);
                uint hdr_default_segments = BitConverter.ToUInt32(hdr, 16);
                float hdr_default_thickness = BitConverter.ToSingle(hdr, 20);
                float hdr_default_transparency = BitConverter.ToSingle(hdr, 24);

                flags_ = hdr_flags;
                default_thickness_ = hdr_default_thickness;
                default_transparency_ = hdr_default_transparency;
                default_segments_ = (int)hdr_default_segments;
                default_color_[0] = BitConverter.ToSingle(hdr, 28);
                default_color_[1] = BitConverter.ToSingle(hdr, 32);
                default_color_[2] = BitConverter.ToSingle(hdr, 36);

                bool has_segments = (flags_ & 0x1) != 0;
                bool has_points = (flags_ & 0x2) != 0;
                bool has_thickness = (flags_ & 0x4) != 0;
                bool has_transparency = (flags_ & 0x8) != 0;
                bool has_color = (flags_ & 0x10) != 0;

                num_strands_ = hdr_num_strands;
                total_points_ = hdr_total_points;

                if (!has_points)
                {
                    Console.Write("No point data in CyHair.\n");
                    return false;
                }

                if ((default_segments_ < 1) && (!has_segments))
                {
                    Console.Write("No valid segment information in CyHair.\n");
                    return false;
                }

                // First read all strand data from a file.
                if (has_segments)
                {
                    segments_ = new ushort[num_strands_];
                    byte[] buf = new byte[2 * num_strands_];
                    if (!ReadAll(fp, buf, buf.Length))
                    {
                        Console.Write("Failed to read CyHair segments data.\n");
                        return false;
                    }
                    Buffer.BlockCopy(buf, 0, segments_, 0, buf.Length);
                }

                if (has_points)
                {
                    Console.Write("[CyHair] Has points.\n");
                    points_ = new float[3 * total_points_];
                    byte[] buf = new byte[total_points_ * 4 * 3];
                    if (!ReadAll(fp, buf, buf.Length))
                    {
                        Console.Write("Failed to read CyHair points data.\n");
                        return false;
                    }
                    Buffer.BlockCopy(buf, 0, points_, 0, buf.Length);
                }
                if (has_thickness)
                {
                    Console.Write("[CyHair] Has thickness.\n");
                    thicknesses_ = new float[total_points_];
                    byte[] buf = new byte[total_points_ * 4];
                    if (!ReadAll(fp, buf, buf.Length))
                    {
                        Console.Write("Failed to read CyHair thickness data.\n");
                        return false;
                    }
                    Buffer.BlockCopy(buf, 0, thicknesses_, 0, buf.Length);
                }

                if (has_transparency)
                {
                    Console.Write("[CyHair] Has transparency.\n");
                    transparencies_ = new float[total_points_];
                    byte[] buf = new byte[total_points_ * 4];
                    if (!ReadAll(fp, buf, buf.Length))
                    {
                        Console.Write("Failed to read CyHair transparencies data.\n");
                        return false;
                    }
                    Buffer.BlockCopy(buf, 0, transparencies_, 0, buf.Length);
                }

                if (has_color)
                {
                    Console.Write("[CyHair] Has color.\n");
                    colors_ = new float[3 * total_points_];
                    byte[] buf = new byte[total_points_ * 4 * 3];
                    if (!ReadAll(fp, buf, buf.Length))
                    {
                        Console.Write("Failed to read CyHair colors data.\n");
                        return false;
                    }
                    Buffer.BlockCopy(buf, 0, colors_, 0, buf.Length);
                }
            }

            // Build strand offset table.
            strand_offsets_ = new uint[num_strands_];
            strand_offsets_[0] = 0;
            for (ulong i = 1; i < num_strands_; i++)
            {
                int num_segments = segments_.Length == 0 ? default_segments_ : segments_[i - 1];
                strand_offsets_[i] = strand_offsets_[i - 1] + (uint)(num_segments + 1);
            }

            return true;
        }

        /// Convert to cubic bezier curves.
        /// 4(cubic) * 3(xyz) * num_curves = vertices.size()
        /// 4(cubic) * num_curves = radiuss.size()
        /// `max_strands` limits the number of strands to convert. -1 = convert all
        /// strands.
        /// `thickness` overwrites strand thickness if it have positive value.
        /// Apply `vertex_translate` after `vertex_scale`.
        public bool ToCubicBezierCurves(List<float> vertices, List<float> radiuss,
                                        float[] vertex_scale, float[] vertex_translate,
                                        int max_strands = -1, float user_thickness = -1.0f)
        {
            if (points_.Length == 0 || strand_offsets_.Length == 0)
            {
                return false;
            }

            vertices.Clear();
            radiuss.Clear();

            int num_strands = (int)num_strands_;

            if ((max_strands > 0) && (max_strands < num_strands))
            {
                num_strands = max_strands;
            }

            Console.Write("[Hair] Convert first " + num_strands + " strands from " +
                          max_strands + " strands in the original hair data.\n");

            // Assume input points are CatmullRom spline.
            for (ulong i = 0; i < (ulong)num_strands; i++)
            {
                if ((i % 1000) == 0)
                {
                    Console.Write(i + " / " + num_strands_ + "\n");
                }

                int num_segments = segments_.Length == 0 ? default_segments_ : segments_[i];
                if (num_segments < 2)
                {
                    continue;
                }

                List<real3> segment_points = new List<real3>();
                for (ulong k = 0; k < (ulong)num_segments; k++)
                {
                    // Zup -> Yup
                    real3 p = new real3(points_[3 * (strand_offsets_[i] + k) + 0],
                                        points_[3 * (strand_offsets_[i] + k) + 2],
                                        points_[3 * (strand_offsets_[i] + k) + 1]);
                    segment_points.Add(p);
                }
                real3[] spa = segment_points.ToArray();

                // Skip both endpoints
                for (int s = 1; s < num_segments - 1; s++)
                {
                    int seg_idx = s - 1;
                    real3[] q = new real3[4];
                    CamullRomToCubicBezier(q, spa, num_segments, seg_idx);

                    for (int c = 0; c < 4; ++c)
                    {
                        vertices.Add(vertex_scale[0] * q[c].x + vertex_translate[0]);
                        vertices.Add(vertex_scale[1] * q[c].y + vertex_translate[1]);
                        vertices.Add(vertex_scale[2] * q[c].z + vertex_translate[2]);
                    }

                    if (user_thickness > 0)
                    {
                        // Use user supplied thickness.
                        radiuss.Add(user_thickness);
                        radiuss.Add(user_thickness);
                        radiuss.Add(user_thickness);
                        radiuss.Add(user_thickness);
                    }
                    else
                    {
                        // TODO(syoyo) Support per point/segment thickness
                        radiuss.Add(default_thickness_);
                        radiuss.Add(default_thickness_);
                        radiuss.Add(default_thickness_);
                        radiuss.Add(default_thickness_);
                    }
                }
            }

            return true;
        }

        // Raw CyHair values
        public ushort[] segments_ = new ushort[0];
        public float[] points_ = new float[0];  // xyz
        public float[] thicknesses_ = new float[0];
        public float[] transparencies_ = new float[0];
        public float[] colors_ = new float[0];  // rgb
        public uint flags_;
        public uint num_strands_;
        public uint total_points_;
        public int default_segments_;
        public float default_thickness_;
        public float default_transparency_;
        public float[] default_color_ = new float[3];

        // Processed CyHair values
        public uint[] strand_offsets_ = new uint[0];
    }
}

///////////////////////////////////////////////////////////////////////////
// The above is cyhair_loader.{h,cc} basically directly; pbrt specific
// code follows...

public static class Cyhair2PbrtMain
{
    static string F(double v) { return v.ToString("F6", CultureInfo.InvariantCulture); }

    public static int Main(string[] argv)
    {
        int argc = argv.Length + 1;
        if (argc <= 2 || argv[0] == "--help" || argv[0] == "-h")
        {
            Console.Error.Write("usage: cyhair2pbrt [CyHair filename] [pbrt output filename] " +
                                "(max strands) (thickness)\n");
            return 1;
        }

        TextWriter f;
        try
        {
            f = (argv[1] == "-") ? Console.Out : new StreamWriter(argv[1], false, new UTF8Encoding(false));
        }
        catch (Exception e)
        {
            Console.Error.Write(argv[1] + ": " + e.Message + "\n");
            return 1;
        }

        int max_strands = -1;         // -1 = Convert all strands
        float user_thickness = 1.0f;  // -1 = Use thickness in CyHair file.
        if (argc > 3)
        {
            int.TryParse(argv[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out max_strands);
        }

        if (argc > 4)
        {
            float.TryParse(argv[3], NumberStyles.Float, CultureInfo.InvariantCulture, out user_thickness);
        }

        cyhair.CyHair hair = new cyhair.CyHair();
        bool ret = hair.Load(argv[0]);
        if (!ret)
        {
            Console.Error.Write("Failed to load CyHair file [ " + argv[0] + " ]\n");
            return 1;
        }

        List<float> pointsL = new List<float>();
        List<float> radiussL = new List<float>();
        float[] vertex_scale = { 1.0f, 1.0f, 1.0f };
        float[] vertex_translate = { 0.0f, 0.0f, 0.0f };
        ret = hair.ToCubicBezierCurves(pointsL, radiussL, vertex_scale, vertex_translate,
                                       max_strands, user_thickness);
        if (!ret)
        {
            Console.Error.Write("Failed to convert CyHair data\n");
            return 1;
        }
        float[] points = pointsL.ToArray();
        float[] radiuss = radiussL.ToArray();

        double[,] bounds = { { 1e30, 1e30, 1e30 }, { -1e30, -1e30, -1e30 } };
        for (int i = 0; i < points.Length / 3; ++i)
        {
            double thickness = (double)radiuss[i];
            for (int c = 0; c < 3; ++c)
            {
                bounds[0, c] = Math.Min(bounds[0, c], (double)points[3 * i + c] - thickness);
                bounds[1, c] = Math.Max(bounds[1, c], (double)points[3 * i + c] + thickness);
            }
        }
        f.Write("# Converted from \"" + argv[0] + "\" by cyhair2pbrt\n");
        f.Write("# The number of strands = " + (radiuss.Length / 4) + ". user_thickness = " +
                F((double)user_thickness) + "\n");
        f.Write("# Scene bounds: (" + F(bounds[0, 0]) + ", " + F(bounds[0, 1]) + ", " + F(bounds[0, 2]) +
                ") - (" + F(bounds[1, 0]) + ", " + F(bounds[1, 1]) + ", " + F(bounds[1, 2]) + ")\n\n\n");

        int num_curves = radiuss.Length / 4;
        for (int i = 0; i < num_curves; i++)
        {
            f.Write("Shape \"curve\" \"string type\" [ \"cylinder\" ] \"point3 P\" [ ");
            for (int j = 0; j < 12; j++)
            {
                f.Write(F((double)points[12 * i + j]) + " ");
            }
            f.Write(" ] \"float width0\" [ " + F((double)radiuss[4 * i + 0]) +
                    " ] \"float width1\" [ " + F((double)radiuss[4 * i + 3]) + " ]\n");
        }

        if (f != Console.Out)
            f.Dispose();
        else
            f.Flush();

        Console.Error.Write("Converted " + (radiuss.Length / 4) + " strands.\n");

        return 0;
    }
}
