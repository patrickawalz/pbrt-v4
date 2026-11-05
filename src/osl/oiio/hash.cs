// OSL port: OpenImageIO/hash.h and OpenImageIO/detail/farmhash.h (the subset the OSL runtime library needs)
//
//  * bjhash: Bob Jenkins' lookup3 mix/final, used by the noise functions to hash lattice coordinates.
//  * farmhash: the 64-bit string hash behind OIIO::Strutil::strhash and therefore ustring::hash() and every OSL::Hashes constant.
//    OIIO's inlined (constexpr) Hash64 is farmhashxo::Hash64 (the SSE4.2 farmhashte path is disabled for constexpr use), with
//    farmhashna / farmhashuo helpers for the length ranges xo hands over to them. Empty strings hash to 0 (Strutil::strhash).

using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace OIIO
{
    public static class bjhash
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint rotl32(uint x, int k)
        {
            return (x << k) | (x >> (32 - k));
        }

        // Mix up the bits of a, b, and c (changing their values in place).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void bjmix(ref uint a, ref uint b, ref uint c)
        {
            a -= c; a ^= rotl32(c, 4); c += b;
            b -= a; b ^= rotl32(a, 6); a += c;
            c -= b; c ^= rotl32(b, 8); b += a;
            a -= c; a ^= rotl32(c, 16); c += b;
            b -= a; b ^= rotl32(a, 19); a += c;
            c -= b; c ^= rotl32(b, 4); b += a;
        }

        // Mix up and combine the bits of a, b, and c (doesn't change them, but returns a hash of those three original values).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint bjfinal(uint a, uint b, uint c)
        {
            c ^= b; c -= rotl32(b, 14);
            a ^= c; a -= rotl32(c, 11);
            b ^= a; b -= rotl32(a, 25);
            c ^= b; c -= rotl32(b, 16);
            a ^= c; a -= rotl32(c, 4);
            b ^= a; b -= rotl32(a, 14);
            c ^= b; c -= rotl32(b, 24);
            return c;
        }
    }

    public static unsafe class farmhash
    {
        private const ulong k0 = 0xc3a5c85c97cb3127UL;
        private const ulong k1 = 0xb492b66fbe98f273UL;
        private const ulong k2 = 0x9ae16a3b2f90404fUL;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Fetch64(byte* p)
        {
            return (ulong)p[0] | ((ulong)p[1] << 8) | ((ulong)p[2] << 16) | ((ulong)p[3] << 24)
                 | ((ulong)p[4] << 32) | ((ulong)p[5] << 40) | ((ulong)p[6] << 48) | ((ulong)p[7] << 56);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Fetch32(byte* p)
        {
            return (uint)p[0] | ((uint)p[1] << 8) | ((uint)p[2] << 16) | ((uint)p[3] << 24);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Rotate(ulong val, int shift)
        {
            return shift == 0 ? val : ((val >> shift) | (val << (64 - shift)));
        }

        private static ulong ShiftMix(ulong val)
        {
            return val ^ (val >> 47);
        }

        // Hash128to64 of (u, v)
        private static ulong HashLen16(ulong u, ulong v)
        {
            const ulong kMul = 0x9ddfea08eb382d69UL;
            ulong a = (u ^ v) * kMul;
            a ^= (a >> 47);
            ulong b = (v ^ a) * kMul;
            b ^= (b >> 47);
            b *= kMul;
            return b;
        }

        private static ulong HashLen16(ulong u, ulong v, ulong mul)
        {
            // Murmur-inspired hashing.
            ulong a = (u ^ v) * mul;
            a ^= (a >> 47);
            ulong b = (v ^ a) * mul;
            b ^= (b >> 47);
            b *= mul;
            return b;
        }

        private static ulong HashLen0to16(byte* s, ulong len)
        {
            if (len >= 8)
            {
                ulong mul = k2 + len * 2;
                ulong a = Fetch64(s) + k2;
                ulong b = Fetch64(s + len - 8);
                ulong c = Rotate(b, 37) * mul + a;
                ulong d = (Rotate(a, 25) + b) * mul;
                return HashLen16(c, d, mul);
            }
            if (len >= 4)
            {
                ulong mul = k2 + len * 2;
                ulong a = Fetch32(s);
                return HashLen16(len + (a << 3), Fetch32(s + len - 4), mul);
            }
            if (len > 0)
            {
                byte a = s[0];
                byte b = s[len >> 1];
                byte c = s[len - 1];
                uint y = (uint)a + ((uint)b << 8);
                uint z = (uint)len + ((uint)c << 2);
                return ShiftMix(y * k2 ^ z * k0) * k2;
            }
            return k2;
        }

        // This probably works well for 16-byte strings as well, but it may be overkill in that case.
        private static ulong HashLen17to32(byte* s, ulong len)
        {
            ulong mul = k2 + len * 2;
            ulong a = Fetch64(s) * k1;
            ulong b = Fetch64(s + 8);
            ulong c = Fetch64(s + len - 8) * mul;
            ulong d = Fetch64(s + len - 16) * k2;
            return HashLen16(Rotate(a + b, 43) + Rotate(c, 30) + d, a + Rotate(b + k2, 18) + c, mul);
        }

        // Return a 16-byte hash for 48 bytes.  Quick and dirty.
        private static void WeakHashLen32WithSeeds(ulong w, ulong x, ulong y, ulong z, ulong a, ulong b, out ulong first, out ulong second)
        {
            a += w;
            b = Rotate(b + a + z, 21);
            ulong c = a;
            a += x;
            a += y;
            b += Rotate(a, 44);
            first = a + z;
            second = b + c;
        }

        private static void WeakHashLen32WithSeeds(byte* s, ulong a, ulong b, out ulong first, out ulong second)
        {
            WeakHashLen32WithSeeds(Fetch64(s), Fetch64(s + 8), Fetch64(s + 16), Fetch64(s + 24), a, b, out first, out second);
        }

        // farmhashna::Hash64WithSeeds / Hash64
        private static ulong NaHashLen33to64(byte* s, ulong len)
        {
            ulong mul = k2 + len * 2;
            ulong a = Fetch64(s) * k2;
            ulong b = Fetch64(s + 8);
            ulong c = Fetch64(s + len - 8) * mul;
            ulong d = Fetch64(s + len - 16) * k2;
            ulong y = Rotate(a + b, 43) + Rotate(c, 30) + d;
            ulong z = HashLen16(y, a + Rotate(b + k2, 18) + c, mul);
            ulong e = Fetch64(s + 16) * mul;
            ulong f = Fetch64(s + 24);
            ulong g = (y + Fetch64(s + len - 32)) * mul;
            ulong h = (z + Fetch64(s + len - 24)) * mul;
            return HashLen16(Rotate(e + f, 43) + Rotate(g, 30) + h, e + Rotate(f + a, 18) + g, mul);
        }

        private static ulong NaHash64(byte* s, ulong len)
        {
            ulong seed = 81;
            if (len <= 32)
            {
                if (len <= 16)
                {
                    return HashLen0to16(s, len);
                }
                else
                {
                    return HashLen17to32(s, len);
                }
            }
            else if (len <= 64)
            {
                return NaHashLen33to64(s, len);
            }

            // For strings over 64 bytes we loop.  Internal state consists of 56 bytes: v, w, x, y, and z.
            ulong x = seed;
            ulong y = seed * k1 + 113;
            ulong z = ShiftMix(y * k2 + 113) * k2;
            ulong vfirst = 0, vsecond = 0;
            ulong wfirst = 0, wsecond = 0;
            x = x * k2 + Fetch64(s);

            // Set end so that after the loop we have 1 to 64 bytes left to process.
            byte* end = s + ((len - 1) / 64) * 64;
            byte* last64 = end + ((len - 1) & 63) - 63;
            do
            {
                x = Rotate(x + y + vfirst + Fetch64(s + 8), 37) * k1;
                y = Rotate(y + vsecond + Fetch64(s + 48), 42) * k1;
                x ^= wsecond;
                y += vfirst + Fetch64(s + 40);
                z = Rotate(z + wfirst, 33) * k1;
                WeakHashLen32WithSeeds(s, vsecond * k1, x + wfirst, out vfirst, out vsecond);
                WeakHashLen32WithSeeds(s + 32, z + wsecond, y + Fetch64(s + 16), out wfirst, out wsecond);
                ulong t = z;
                z = x;
                x = t;
                s += 64;
            }
            while (s != end);
            ulong mul = k1 + ((z & 0xff) << 1);
            // Make s point to the last 64 bytes of input.
            s = last64;
            wfirst += ((len - 1) & 63);
            vfirst += wfirst;
            wfirst += vfirst;
            x = Rotate(x + y + vfirst + Fetch64(s + 8), 37) * mul;
            y = Rotate(y + vsecond + Fetch64(s + 48), 42) * mul;
            x ^= wsecond * 9;
            y += vfirst * 9 + Fetch64(s + 40);
            z = Rotate(z + wfirst, 33) * mul;
            WeakHashLen32WithSeeds(s, vsecond * mul, x + wfirst, out vfirst, out vsecond);
            WeakHashLen32WithSeeds(s + 32, z + wsecond, y + Fetch64(s + 16), out wfirst, out wsecond);
            ulong tt = z;
            z = x;
            x = tt;
            return HashLen16(HashLen16(vfirst, wfirst, mul) + ShiftMix(y) * k0 + z, HashLen16(vsecond, wsecond, mul) + x, mul);
        }

        private static ulong NaHash64WithSeeds(byte* s, ulong len, ulong seed0, ulong seed1)
        {
            return HashLen16(NaHash64(s, len) - seed0, seed1);
        }

        private static ulong NaHash64WithSeed(byte* s, ulong len, ulong seed)
        {
            return NaHash64WithSeeds(s, len, k2, seed);
        }

        // farmhashuo
        private static ulong UoH(ulong x, ulong y, ulong mul, int r)
        {
            ulong a = (x ^ y) * mul;
            a ^= (a >> 47);
            ulong b = (y ^ a) * mul;
            return Rotate(b, r) * mul;
        }

        private static ulong UoHash64WithSeeds(byte* s, ulong len, ulong seed0, ulong seed1)
        {
            if (len <= 64)
            {
                return NaHash64WithSeeds(s, len, seed0, seed1);
            }

            // For strings over 64 bytes we loop.  Internal state consists of 64 bytes: u, v, w, x, y, and z.
            ulong x = seed0;
            ulong y = seed1 * k2 + 113;
            ulong z = ShiftMix(y * k2) * k2;
            ulong vfirst = seed0, vsecond = seed1;
            ulong wfirst = 0, wsecond = 0;
            ulong u = x - z;
            x *= k2;
            ulong mul = k2 + (u & 0x82);

            // Set end so that after the loop we have 1 to 64 bytes left to process.
            byte* end = s + ((len - 1) / 64) * 64;
            byte* last64 = end + ((len - 1) & 63) - 63;
            do
            {
                ulong a0 = Fetch64(s);
                ulong a1 = Fetch64(s + 8);
                ulong a2 = Fetch64(s + 16);
                ulong a3 = Fetch64(s + 24);
                ulong a4 = Fetch64(s + 32);
                ulong a5 = Fetch64(s + 40);
                ulong a6 = Fetch64(s + 48);
                ulong a7 = Fetch64(s + 56);
                x += a0 + a1;
                y += a2;
                z += a3;
                vfirst += a4;
                vsecond += a5 + a1;
                wfirst += a6;
                wsecond += a7;

                x = Rotate(x, 26);
                x *= 9;
                y = Rotate(y, 29);
                z *= mul;
                vfirst = Rotate(vfirst, 33);
                vsecond = Rotate(vsecond, 30);
                wfirst ^= x;
                wfirst *= 9;
                z = Rotate(z, 32);
                z += wsecond;
                wsecond += z;
                z *= 9;
                ulong t1 = u;
                u = y;
                y = t1;

                z += a0 + a6;
                vfirst += a2;
                vsecond += a3;
                wfirst += a4;
                wsecond += a5 + a6;
                x += a1;
                y += a7;

                y += vfirst;
                vfirst += x - y;
                vsecond += wfirst;
                wfirst += vsecond;
                wsecond += x - y;
                x += wsecond;
                wsecond = Rotate(wsecond, 34);
                ulong t2 = u;
                u = z;
                z = t2;
                s += 64;
            }
            while (s != end);
            // Make s point to the last 64 bytes of input.
            s = last64;
            u *= 9;
            vsecond = Rotate(vsecond, 28);
            vfirst = Rotate(vfirst, 20);
            wfirst += ((len - 1) & 63);
            u += y;
            y += u;
            x = Rotate(y - x + vfirst + Fetch64(s + 8), 37) * mul;
            y = Rotate(y ^ vsecond ^ Fetch64(s + 48), 42) * mul;
            x ^= wsecond * 9;
            y += vfirst + Fetch64(s + 40);
            z = Rotate(z + wfirst, 33) * mul;
            WeakHashLen32WithSeeds(s, vsecond * mul, x + wfirst, out vfirst, out vsecond);
            WeakHashLen32WithSeeds(s + 32, z + wsecond, y + Fetch64(s + 16), out wfirst, out wsecond);
            return UoH(HashLen16(vfirst + x, wfirst ^ y, mul) + z - u, UoH(vsecond + y, wsecond + z, k2, 30) ^ x, k2, 31);
        }

        private static ulong UoHash64(byte* s, ulong len)
        {
            return len <= 64 ? NaHash64(s, len) : UoHash64WithSeeds(s, len, 81, 0);
        }

        // farmhashxo
        private static ulong XoH32(byte* s, ulong len, ulong mul, ulong seed0, ulong seed1)
        {
            ulong a = Fetch64(s) * k1;
            ulong b = Fetch64(s + 8);
            ulong c = Fetch64(s + len - 8) * mul;
            ulong d = Fetch64(s + len - 16) * k2;
            ulong u = Rotate(a + b, 43) + Rotate(c, 30) + d + seed0;
            ulong v = a + Rotate(b + k2, 18) + c + seed1;
            a = ShiftMix((u ^ v) * mul);
            b = ShiftMix((v ^ a) * mul);
            return b;
        }

        // Return an 8-byte hash for 33 to 64 bytes.
        private static ulong XoHashLen33to64(byte* s, ulong len)
        {
            ulong mul0 = k2 - 30;
            ulong mul1 = k2 - 30 + 2 * len;
            ulong h0 = XoH32(s, 32, mul0, 0, 0);
            ulong h1 = XoH32(s + len - 32, 32, mul1, 0, 0);
            return ((h1 * mul1) + h0) * mul1;
        }

        // Return an 8-byte hash for 65 to 96 bytes.
        private static ulong XoHashLen65to96(byte* s, ulong len)
        {
            ulong mul0 = k2 - 114;
            ulong mul1 = k2 - 114 + 2 * len;
            ulong h0 = XoH32(s, 32, mul0, 0, 0);
            ulong h1 = XoH32(s + 32, 32, mul1, 0, 0);
            ulong h2 = XoH32(s + len - 32, 32, mul1, h0, h1);
            return (h2 * 9 + (h0 >> 17) + (h1 >> 21)) * mul1;
        }

        // OIIO::farmhash::inlined::Hash64 (as used by Strutil::strhash64 for non-empty strings)
        public static ulong Hash64(byte* s, ulong len)
        {
            if (len <= 32)
            {
                if (len <= 16)
                {
                    return HashLen0to16(s, len);
                }
                else
                {
                    return HashLen17to32(s, len);
                }
            }
            else if (len <= 64)
            {
                return XoHashLen33to64(s, len);
            }
            else if (len <= 96)
            {
                return XoHashLen65to96(s, len);
            }
            else if (len <= 256)
            {
                return NaHash64(s, len);
            }
            else
            {
                return UoHash64(s, len);
            }
        }

        public static ulong Hash64(byte[] data)
        {
            fixed (byte* p = data)
            {
                return Hash64(p, (ulong)data.Length);
            }
        }
    }

    public static class Strutil_hash
    {
        // OIIO::Strutil::strhash64: farmhash of the UTF-8 bytes; the empty string always hashes to 0
        public static unsafe ulong strhash64(string s)
        {
            if (s == null || s.Length == 0)
            {
                return 0;
            }
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            return farmhash.Hash64(bytes);
        }
    }
}
