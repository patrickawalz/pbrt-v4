// PtexHalf.h, PtexHalf.cpp
/*
PTEX SOFTWARE
Copyright 2014 Disney Enterprises, Inc.  All rights reserved
(see LICENSE.txt in ext/ptex)
*/

using System;

namespace Ptex
{
    // Half-precision (16-bit) floating-point type. This type should be compatible with
    // opengl, openexr, and IEEE 754r. The range is [-65504.0, 65504.0] and the precision is
    // about 1 part in 2000 (3.3 decimal places).
    public struct PtexHalf
    {
        public ushort bits;

        public PtexHalf(float val) { bits = fromFloat(val); }
        public PtexHalf(double val) { bits = fromFloat((float)val); }
        public static implicit operator float(PtexHalf h) { return toFloat(h.bits); }
        public static implicit operator PtexHalf(float v) { return new PtexHalf(v); }

        public static float toFloat(ushort h)
        {
            return BitConverter.UInt32BitsToSingle(PtexHalfTables.h2fTable[h]);
        }

        public static ushort fromFloat(float val)
        {
            if (val == 0) return 0;
            uint i = BitConverter.SingleToUInt32Bits(val);
            int e = PtexHalfTables.f2hTable[(i >> 23) & 0x1ff];
            if (e != 0) return (ushort)(e + (((i & 0x7fffff) + 0x1000) >> 13));
            return fromFloat_except(i);
        }

        // Handle exceptional cases for half-to-float conversion
        static ushort fromFloat_except(uint i)
        {
            uint s = ((i >> 16) & 0x8000);
            int e = (int)((i >> 13) & 0x3fc00) - 0x1c000;

            if (e <= 0)
            {
                // denormalized
                float f = BitConverter.UInt32BitsToSingle(i);
                return (ushort)(s | (uint)(int)(Math.Abs((double)f) * 1.6777216e7 + .5));
            }

            if (e == 0x23c00)
                // inf/nan, preserve msb bits of m for nan code
                return (ushort)(s | 0x7c00 | ((i & 0x7fffff) >> 13));
            else
                // overflow - convert to inf
                return (ushort)(s | 0x7c00);
        }
    }
}
