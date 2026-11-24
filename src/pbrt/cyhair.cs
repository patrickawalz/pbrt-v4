// (not in pbrt-v4) cyhair.cs
//
// Strands of hair as they come from a groom, and the HAIR file of Cem Yuksel (www.cemyuksel.com/research/hairmodels) that
// holds them: a header of 128 bytes
//
//   char[4]   "HAIR"
//   uint32    the number of strands
//   uint32    the number of points of all the strands
//   uint32    which arrays follow: 1 segments, 2 points, 4 thickness, 8 transparency, 16 colors
//   uint32    the segments of a strand without the segments array
//   float     the thickness without the thickness array
//   float     the transparency without the transparency array
//   float[3]  the color without the color array
//   char[88]  a note
//
// and then, little-endian, the arrays that the header names, in that order: uint16 segments for each strand (a strand of n
// segments has n + 1 points), float[3] for each point, float thickness for each point, float transparency for each point,
// float[3] color for each point.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace pbrt
{
    // HairStrandSet Definition
    public sealed class HairStrandSet
    {
        // HairStrandSet Public Members

        // The points of the strand i are those from first[i] up to first[i + 1] (first has one more value than there are strands)
        public int[] first;
        // x, y, z for each point
        public float[] points;
        // For each point, or null: then the one value for all
        public float[] thickness;
        public float[] transparency;
        // r, g, b for each point, or null
        public float[] colors;
        public float defaultThickness = 1;
        public float defaultTransparency;
        public float defaultRed = 1, defaultGreen = 1, defaultBlue = 1;
        public string note = "";

        // HairStrandSet Public Methods
        public int StrandCount
        {
            get { return first.Length - 1; }
        }

        public int PointCount
        {
            get { return points.Length / 3; }
        }
    }

    // CyHair Definition
    public static class CyHair
    {
        // CyHair Public Methods
        public static HairStrandSet Read(string filename)
        {
            byte[] bytes = File.ReadAllBytes(filename);
            if (bytes.Length < HeaderSize || bytes[0] != (byte)'H' || bytes[1] != (byte)'A' || bytes[2] != (byte)'I' || bytes[3] != (byte)'R')
            {
                pbrt.ErrorExit(filename + ": not a HAIR file.");
            }
            ReadOnlySpan<byte> header = new ReadOnlySpan<byte>(bytes, 0, HeaderSize);
            long strands = MemoryMarshal.Read<uint>(header.Slice(4)), points = MemoryMarshal.Read<uint>(header.Slice(8));
            uint arrays = MemoryMarshal.Read<uint>(header.Slice(12)), defaultSegments = MemoryMarshal.Read<uint>(header.Slice(16));
            HairStrandSet set = new HairStrandSet();
            set.defaultThickness = MemoryMarshal.Read<float>(header.Slice(20));
            set.defaultTransparency = MemoryMarshal.Read<float>(header.Slice(24));
            set.defaultRed = MemoryMarshal.Read<float>(header.Slice(28));
            set.defaultGreen = MemoryMarshal.Read<float>(header.Slice(32));
            set.defaultBlue = MemoryMarshal.Read<float>(header.Slice(36));
            int noteLength = 0;
            while (noteLength < 88 && bytes[40 + noteLength] != 0)
            {
                ++noteLength;
            }
            set.note = Encoding.ASCII.GetString(bytes, 40, noteLength);
            if ((arrays & PointsBit) == 0)
            {
                pbrt.ErrorExit(filename + ": a HAIR file without points.");
            }
            long size = HeaderSize + ((arrays & SegmentsBit) != 0 ? 2 * strands : 0) + 12 * points + ((arrays & ThicknessBit) != 0 ? 4 * points : 0) +
                        ((arrays & TransparencyBit) != 0 ? 4 * points : 0) + ((arrays & ColorsBit) != 0 ? 12 * points : 0);
            if (strands > int.MaxValue - 1 || 3 * points > int.MaxValue || bytes.Length < size)
            {
                pbrt.ErrorExit(filename + ": the HAIR file ends before its arrays do (" + bytes.Length + " bytes of " + size + ").");
            }
            int at = HeaderSize;
            set.first = new int[strands + 1];
            if ((arrays & SegmentsBit) != 0)
            {
                ReadOnlySpan<ushort> segments = MemoryMarshal.Cast<byte, ushort>(new ReadOnlySpan<byte>(bytes, at, (int)(2 * strands)));
                for (int i = 0; i < strands; ++i)
                {
                    set.first[i + 1] = set.first[i] + segments[i] + 1;
                }
                at += (int)(2 * strands);
            }
            else
            {
                for (int i = 0; i < strands; ++i)
                {
                    set.first[i + 1] = set.first[i] + (int)defaultSegments + 1;
                }
            }
            if (set.first[strands] != points)
            {
                pbrt.ErrorExit(filename + ": the strands of the HAIR file have " + set.first[strands] + " points, its header " + points + ".");
            }
            set.points = Floats(bytes, ref at, (int)(3 * points));
            if ((arrays & ThicknessBit) != 0)
            {
                set.thickness = Floats(bytes, ref at, (int)points);
            }
            if ((arrays & TransparencyBit) != 0)
            {
                set.transparency = Floats(bytes, ref at, (int)points);
            }
            if ((arrays & ColorsBit) != 0)
            {
                set.colors = Floats(bytes, ref at, (int)(3 * points));
            }
            return set;
        }

        // (the segments array is left out where all the strands have the same number of points)
        public static void Write(string filename, HairStrandSet set)
        {
            int strands = set.StrandCount, points = set.PointCount;
            bool even = true;
            for (int i = 1; i < strands; ++i)
            {
                even &= set.first[i + 1] - set.first[i] == set.first[1] - set.first[0];
            }
            uint arrays = PointsBit | (even ? 0 : SegmentsBit) | (set.thickness != null ? ThicknessBit : 0) | (set.transparency != null ? TransparencyBit : 0) |
                          (set.colors != null ? ColorsBit : 0);
            using (FileStream stream = new FileStream(filename, FileMode.Create, FileAccess.Write))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(new byte[] { (byte)'H', (byte)'A', (byte)'I', (byte)'R' });
                writer.Write((uint)strands);
                writer.Write((uint)points);
                writer.Write(arrays);
                writer.Write((uint)(strands > 0 ? set.first[1] - set.first[0] - 1 : 0));
                writer.Write(set.defaultThickness);
                writer.Write(set.defaultTransparency);
                writer.Write(set.defaultRed);
                writer.Write(set.defaultGreen);
                writer.Write(set.defaultBlue);
                byte[] note = new byte[88];
                Encoding.ASCII.GetBytes(set.note, 0, Math.Min(87, set.note.Length), note, 0);
                writer.Write(note);
                if (!even)
                {
                    for (int i = 0; i < strands; ++i)
                    {
                        writer.Write((ushort)(set.first[i + 1] - set.first[i] - 1));
                    }
                }
                writer.Write(MemoryMarshal.AsBytes(new ReadOnlySpan<float>(set.points)));
                if (set.thickness != null)
                {
                    writer.Write(MemoryMarshal.AsBytes(new ReadOnlySpan<float>(set.thickness)));
                }
                if (set.transparency != null)
                {
                    writer.Write(MemoryMarshal.AsBytes(new ReadOnlySpan<float>(set.transparency)));
                }
                if (set.colors != null)
                {
                    writer.Write(MemoryMarshal.AsBytes(new ReadOnlySpan<float>(set.colors)));
                }
            }
        }

        // CyHair Private Methods
        private static float[] Floats(byte[] bytes, ref int at, int count)
        {
            float[] values = new float[count];
            MemoryMarshal.Cast<byte, float>(new ReadOnlySpan<byte>(bytes, at, 4 * count)).CopyTo(values);
            at += 4 * count;
            return values;
        }

        // CyHair Private Members
        private const int HeaderSize = 128;
        private const uint SegmentsBit = 1, PointsBit = 2, ThicknessBit = 4, TransparencyBit = 8, ColorsBit = 16;
    }
}
