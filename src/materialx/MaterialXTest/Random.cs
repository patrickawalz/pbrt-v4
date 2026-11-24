// (not in MaterialX) Random.cs: the random numbers the tests draw with the
// C++ standard library.

namespace MaterialXTest
{
    // mt19937, as std::mt19937 produces its numbers
    public sealed class mt19937
    {
        public mt19937(uint seed)
        {
            _state[0] = seed;
            for (int i = 1; i < 624; i++)
            {
                _state[i] = 1812433253u * (_state[i - 1] ^ (_state[i - 1] >> 30)) + (uint)i;
            }
            _index = 624;
        }

        public uint next()
        {
            if (_index >= 624)
            {
                for (int i = 0; i < 624; i++)
                {
                    uint y = (_state[i] & 0x80000000u) | (_state[(i + 1) % 624] & 0x7fffffffu);
                    _state[i] = _state[(i + 397) % 624] ^ (y >> 1) ^ ((y & 1u) != 0 ? 0x9908b0dfu : 0u);
                }
                _index = 0;
            }
            uint x = _state[_index++];
            x ^= x >> 11;
            x ^= (x << 7) & 0x9d2c5680u;
            x ^= (x << 15) & 0xefc60000u;
            x ^= x >> 18;
            return x;
        }

        private uint[] _state = new uint[624];
        private int _index;
    }

    // std::uniform_int_distribution<int>(a, b) drawing from a 32 bit generator,
    // as libstdc++ computes it
    public static class StdRandom
    {
        public static int uniformInt(mt19937 rng, int a, int b)
        {
            uint range = (uint)(b - a);
            if (range == 0xffffffffu)
            {
                return (int)(rng.next() + (uint)a);
            }
            uint size = range + 1;
            ulong product = (ulong)rng.next() * size;
            uint low = (uint)product;
            if (low < size)
            {
                uint threshold = (0u - size) % size;
                while (low < threshold)
                {
                    product = (ulong)rng.next() * size;
                    low = (uint)product;
                }
            }
            return (int)((uint)(product >> 32) + (uint)a);
        }

        // std::shuffle is specific to each standard library. This is the
        // plain Fisher-Yates shuffle drawing from the same generator.
        public static void shuffle<T>(System.Collections.Generic.List<T> list, mt19937 rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = uniformInt(rng, 0, i);
                T swap = list[i];
                list[i] = list[j];
                list[j] = swap;
            }
        }
    }
}
