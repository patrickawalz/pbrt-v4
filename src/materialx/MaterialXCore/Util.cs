// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXCore/Library.h,
// Generated.h, Util.h and Util.cpp.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MaterialX
{
    // StringMap Definition
    // std::unordered_map<string, string>; this port iterates in insertion order.
    public class StringMap : Dictionary<string, string>
    {
        public StringMap() : base(StringComparer.Ordinal)
        {
        }

        public StringMap(StringMap other) : base(other, StringComparer.Ordinal)
        {
        }
    }

    // StringSet Definition
    // std::set<string>, ordered by byte value.
    public class StringSet : SortedSet<string>
    {
        public StringSet() : base(StringComparer.Ordinal)
        {
        }

        public StringSet(IEnumerable<string> items) : base(items, StringComparer.Ordinal)
        {
        }
    }

    // Utility Functions
    public static partial class Mx
    {
        public const int MATERIALX_MAJOR_VERSION = 1;
        public const int MATERIALX_MINOR_VERSION = 39;
        public const int MATERIALX_BUILD_VERSION = 6;

        // MATERIALX_GENERATE_INDEX
        public static int MATERIALX_GENERATE_INDEX(int major, int minor, int build)
        {
            return (major << 22) | (minor << 12) | build;
        }

        public const int MATERIALX_VERSION_INDEX = (MATERIALX_MAJOR_VERSION << 22) | (MATERIALX_MINOR_VERSION << 12) | MATERIALX_BUILD_VERSION;

        public const string EMPTY_STRING = "";

        public static string getVersionString()
        {
            return MATERIALX_MAJOR_VERSION.ToString(CultureInfo.InvariantCulture) + "." +
                   MATERIALX_MINOR_VERSION.ToString(CultureInfo.InvariantCulture) + "." +
                   MATERIALX_BUILD_VERSION.ToString(CultureInfo.InvariantCulture);
        }

        public static (int, int, int) getVersionIntegers()
        {
            return (MATERIALX_MAJOR_VERSION, MATERIALX_MINOR_VERSION, MATERIALX_BUILD_VERSION);
        }

        internal static bool isalnum(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        internal static bool isdigit(char c)
        {
            return c >= '0' && c <= '9';
        }

        private static bool invalidNameChar(char c)
        {
            return !isalnum(c) && c != '_' && c != ':';
        }

        public static string createValidName(string name, char replaceChar = '_')
        {
            // The C++ works on bytes, so every byte of a multi-byte character is replaced.
            StringBuilder result = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (c < 0x80)
                {
                    result.Append(invalidNameChar(c) ? replaceChar : c);
                }
                else
                {
                    int bytes = c < 0x800 ? 2 : (char.IsSurrogate(c) ? 2 : 3);
                    result.Append(replaceChar, bytes);
                }
            }
            return result.ToString();
        }

        public static bool isValidName(string name)
        {
            foreach (char c in name)
            {
                if (invalidNameChar(c))
                {
                    return false;
                }
            }
            return true;
        }

        public static string incrementName(string name)
        {
            int split = name.Length;
            while (split > 0)
            {
                if (!isdigit(name[split - 1]))
                {
                    break;
                }
                split--;
            }

            if (split < name.Length)
            {
                string prefix = name.Substring(0, split);
                string suffix = name.Substring(split);
                return prefix + (int.Parse(suffix, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture);
            }
            return name + "2";
        }

        public static List<string> splitString(string str, string sep)
        {
            List<string> split = new List<string>();

            int lastPos = find_first_not_of(str, sep, 0);
            int pos = find_first_of(str, sep, lastPos);

            while (pos != -1 || lastPos != -1)
            {
                split.Add(pos == -1 ? str.Substring(lastPos) : str.Substring(lastPos, pos - lastPos));
                lastPos = find_first_not_of(str, sep, pos);
                pos = find_first_of(str, sep, lastPos);
            }

            return split;
        }

        // std::string::find_first_not_of, with -1 for npos.
        public static int find_first_not_of(string str, string chars, int start)
        {
            if (start < 0)
            {
                return -1;
            }
            for (int i = start; i < str.Length; i++)
            {
                if (chars.IndexOf(str[i]) < 0)
                {
                    return i;
                }
            }
            return -1;
        }

        // std::string::find_first_of, with -1 for npos.
        public static int find_first_of(string str, string chars, int start)
        {
            if (start < 0)
            {
                return -1;
            }
            for (int i = start; i < str.Length; i++)
            {
                if (chars.IndexOf(str[i]) >= 0)
                {
                    return i;
                }
            }
            return -1;
        }

        public static string joinStrings(List<string> stringVec, string sep)
        {
            string res = stringVec.Count == 0 ? EMPTY_STRING : stringVec[0];
            StringBuilder builder = new StringBuilder(res);
            for (int i = 1; i < stringVec.Count; i++)
            {
                builder.Append(sep);
                builder.Append(stringVec[i]);
            }
            return builder.ToString();
        }

        public static string replaceSubstrings(string str, StringMap stringMap)
        {
            foreach (KeyValuePair<string, string> pair in stringMap)
            {
                if (pair.Key.Length == 0)
                {
                    continue;
                }

                int pos = 0;
                while ((pos = str.IndexOf(pair.Key, pos, StringComparison.Ordinal)) != -1)
                {
                    str = str.Substring(0, pos) + pair.Value + str.Substring(pos + pair.Key.Length);
                    pos += pair.Value.Length;
                }
            }
            return str;
        }

        public static string stringToLower(string str)
        {
            StringBuilder result = new StringBuilder(str.Length);
            foreach (char c in str)
            {
                result.Append(c >= 'A' && c <= 'Z' ? (char)(c + ('a' - 'A')) : c);
            }
            return result.ToString();
        }

        public static bool stringStartsWith(string str, string prefix)
        {
            return str.StartsWith(prefix, StringComparison.Ordinal);
        }

        public static bool stringEndsWith(string str, string suffix)
        {
            return str.EndsWith(suffix, StringComparison.Ordinal);
        }

        public static string trimSpaces(string str)
        {
            return str.Trim(' ');
        }

        // hashCombine for a float (not in MaterialX as a separate overload; the
        // C++ template is instantiated for float by the vector and matrix hashes).
        public static ulong hashCombine(ulong seed, float value)
        {
            ulong hash = value == 0.0f ? 0UL : (ulong)BitConverter.SingleToUInt32Bits(value);
            return seed ^ (hash + 0x9e3779b9UL + (seed << 6) + (seed >> 2));
        }

        public static ulong hashCombine(ulong seed, string value)
        {
            return seed ^ (stringHash(value) + 0x9e3779b9UL + (seed << 6) + (seed >> 2));
        }

        public static ulong hashCombine(ulong seed, ulong value)
        {
            return seed ^ (value + 0x9e3779b9UL + (seed << 6) + (seed >> 2));
        }

        // FNV-1a over the UTF-16 code units; std::hash<string> is unspecified.
        public static ulong stringHash(string value)
        {
            ulong hash = 14695981039346656037UL;
            foreach (char c in value)
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }
            return hash;
        }

        // std::sort as libstdc++ implements it (not in MaterialX). The order in
        // which equal elements come out decides the order of generated code, so
        // the algorithm is reproduced: introsort with a median-of-three pivot,
        // a heap sort once the depth limit is reached, and a final insertion sort.
        public static void stdSort<T>(List<T> list, Func<T, T, bool> less)
        {
            int count = list.Count;
            if (count == 0)
            {
                return;
            }
            int depthLimit = 0;
            for (int n = count; n > 1; n >>= 1)
            {
                depthLimit++;
            }
            introsortLoop(list, 0, count, depthLimit * 2, less);
            finalInsertionSort(list, 0, count, less);
        }

        private const int stdSortThreshold = 16;

        private static void introsortLoop<T>(List<T> list, int first, int last, int depthLimit, Func<T, T, bool> less)
        {
            while (last - first > stdSortThreshold)
            {
                if (depthLimit == 0)
                {
                    heapSort(list, first, last, less);
                    return;
                }
                --depthLimit;
                int cut = unguardedPartitionPivot(list, first, last, less);
                introsortLoop(list, cut, last, depthLimit, less);
                last = cut;
            }
        }

        private static void swapItems<T>(List<T> list, int a, int b)
        {
            T temporary = list[a];
            list[a] = list[b];
            list[b] = temporary;
        }

        private static int unguardedPartitionPivot<T>(List<T> list, int first, int last, Func<T, T, bool> less)
        {
            int mid = first + (last - first) / 2;
            int a = first + 1;
            int b = mid;
            int c = last - 1;
            if (less(list[a], list[b]))
            {
                if (less(list[b], list[c]))
                {
                    swapItems(list, first, b);
                }
                else if (less(list[a], list[c]))
                {
                    swapItems(list, first, c);
                }
                else
                {
                    swapItems(list, first, a);
                }
            }
            else if (less(list[a], list[c]))
            {
                swapItems(list, first, a);
            }
            else if (less(list[b], list[c]))
            {
                swapItems(list, first, c);
            }
            else
            {
                swapItems(list, first, b);
            }

            int pivot = first;
            int left = first + 1;
            int right = last;
            while (true)
            {
                while (less(list[left], list[pivot]))
                {
                    ++left;
                }
                --right;
                while (less(list[pivot], list[right]))
                {
                    --right;
                }
                if (!(left < right))
                {
                    return left;
                }
                swapItems(list, left, right);
                ++left;
            }
        }

        private static void unguardedLinearInsert<T>(List<T> list, int last, Func<T, T, bool> less)
        {
            T value = list[last];
            int next = last - 1;
            while (less(value, list[next]))
            {
                list[last] = list[next];
                last = next;
                --next;
            }
            list[last] = value;
        }

        private static void insertionSort<T>(List<T> list, int first, int last, Func<T, T, bool> less)
        {
            if (first == last)
            {
                return;
            }
            for (int i = first + 1; i != last; ++i)
            {
                if (less(list[i], list[first]))
                {
                    T value = list[i];
                    for (int j = i; j > first; --j)
                    {
                        list[j] = list[j - 1];
                    }
                    list[first] = value;
                }
                else
                {
                    unguardedLinearInsert(list, i, less);
                }
            }
        }

        private static void finalInsertionSort<T>(List<T> list, int first, int last, Func<T, T, bool> less)
        {
            if (last - first > stdSortThreshold)
            {
                insertionSort(list, first, first + stdSortThreshold, less);
                for (int i = first + stdSortThreshold; i != last; ++i)
                {
                    unguardedLinearInsert(list, i, less);
                }
            }
            else
            {
                insertionSort(list, first, last, less);
            }
        }

        private static void adjustHeap<T>(List<T> list, int first, int holeIndex, int len, T value, Func<T, T, bool> less)
        {
            int topIndex = holeIndex;
            int secondChild = holeIndex;
            while (secondChild < (len - 1) / 2)
            {
                secondChild = 2 * (secondChild + 1);
                if (less(list[first + secondChild], list[first + secondChild - 1]))
                {
                    secondChild--;
                }
                list[first + holeIndex] = list[first + secondChild];
                holeIndex = secondChild;
            }
            if ((len & 1) == 0 && secondChild == (len - 2) / 2)
            {
                secondChild = 2 * (secondChild + 1);
                list[first + holeIndex] = list[first + secondChild - 1];
                holeIndex = secondChild - 1;
            }
            int parent = (holeIndex - 1) / 2;
            while (holeIndex > topIndex && less(list[first + parent], value))
            {
                list[first + holeIndex] = list[first + parent];
                holeIndex = parent;
                parent = (holeIndex - 1) / 2;
            }
            list[first + holeIndex] = value;
        }

        private static void heapSort<T>(List<T> list, int first, int last, Func<T, T, bool> less)
        {
            int len = last - first;
            if (len >= 2)
            {
                int parent = (len - 2) / 2;
                while (true)
                {
                    T value = list[first + parent];
                    adjustHeap(list, first, parent, len, value, less);
                    if (parent == 0)
                    {
                        break;
                    }
                    parent--;
                }
            }
            while (last - first > 1)
            {
                --last;
                T value = list[last];
                list[last] = list[first];
                adjustHeap(list, first, 0, last - first, value, less);
            }
        }

        // std::to_string (not in MaterialX).
        public static string to_string(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        public static string to_string(float value)
        {
            return ((double)value).ToString("F6", CultureInfo.InvariantCulture);
        }

        public static List<string> splitNamePath(string namePath)
        {
            List<string> nameVec = splitString(namePath, NAME_PATH_SEPARATOR);
            return nameVec;
        }

        public static string createNamePath(List<string> nameVec)
        {
            string res = joinStrings(nameVec, NAME_PATH_SEPARATOR);
            return res;
        }

        public static string parentNamePath(string namePath)
        {
            List<string> nameVec = splitNamePath(namePath);
            if (nameVec.Count != 0)
            {
                nameVec.RemoveAt(nameVec.Count - 1);
                return createNamePath(nameVec);
            }
            return EMPTY_STRING;
        }
    }
}
