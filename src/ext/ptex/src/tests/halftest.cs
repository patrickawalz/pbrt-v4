// halftest.cpp
/* test code for PtexHalf class */

using System;
using System.Globalization;
using Ptex;

public static unsafe class HalfTest
{
    static float h2f(ushort h)
    {
        return PtexHalf.toFloat(h);
    }

    static ushort f2h(float f)
    {
        return PtexHalf.fromFloat(f);
    }

    static string G(double v, int prec = 6)
    {
        if (double.IsNaN(v)) return "nan";
        if (double.IsInfinity(v)) return v > 0 ? "inf" : "-inf";
        return v.ToString("G" + prec, CultureInfo.InvariantCulture).Replace('E', 'e');
    }

    static uint floatToBits(float f) { return BitConverter.SingleToUInt32Bits(f); }
    static float bitsToFloat(uint bits) { return BitConverter.UInt32BitsToSingle(bits); }

    static int testconvert(int i)
    {
        float f = h2f((ushort)i);
        int i2 = f2h(f);
        if (i != i2)
        {
            Console.Write("error: 0x" + i.ToString("x") + " -> " + G(f) + " -> 0x" + i2.ToString("x") + "\n");
            return 1;
        }
        return 0;
    }

    static int testconvertall()
    {
        int count = 0;
        // just do finite values (skip inf/nan)
        for (int i = 0x0000; i < 0x7c00; i++) count += testconvert(i);
        for (int i = 0x8001; i < 0xfc00; i++) count += testconvert(i);
        return count;
    }

    static int testround(float val)
    {
        int i = f2h(val);
        float f = MathF.Abs(h2f((ushort)i) - val);
        float f1 = MathF.Abs(h2f((ushort)(i - 1)) - val);
        float f2 = MathF.Abs(h2f((ushort)(i + 1)) - val);
        if (f1 < f)
        {
            Console.Write("error: " + G(val) + "->0x" + i.ToString("x") + "->" + G(h2f((ushort)i)) + ", expected ->0x" + (i - 1).ToString("x") + "->" + G(h2f((ushort)(i - 1))) + "\n");
            return 1;
        }
        if (f2 < f)
        {
            Console.Write("error: " + G(val) + "->0x" + i.ToString("x") + "->" + G(h2f((ushort)i)) + ", expected ->0x" + (i + 1).ToString("x") + "->" + G(h2f((ushort)(i + 1))) + "\n");
            return 1;
        }
        return 0;
    }

    static int testroundrange(int inc)
    {
        int count = 0;

        // check all legal float32 values within legal float16 range
        float f1 = (float)2.9802320611338473e-08;  // min float16
        float f2 = 65519;  // max float 16
        uint i1 = floatToBits(f1);
        uint i2 = floatToBits(f2);
        for (uint i = i1; i < i2; i += (uint)inc) count += testround(bitsToFloat(i));

        // and the negatives
        f1 = (float)-2.9802320611338473e-08;  // min float16
        f2 = -65519;  // max float 16
        i1 = floatToBits(f1);
        i2 = floatToBits(f2);
        for (uint i = i1; i < i2; i += (uint)inc) count += testround(bitsToFloat(i));
        return count;
    }

    static int testroundall()
    {
        // can be slow
        return testroundrange(1);
    }

    static int testroundsome()
    {
        // fairly dense sampling is still a good test
        return testroundrange(97);
    }

    static int compatcheck()
    {
        PtexHalf h = 1.5f;
        PtexHalf h2 = 2.5f;
        h = (float)h + (float)h2;
        float f = h;
        if (f != 4) return 1;
        double d = (float)h;
        if (d != 4) return 1;
        h = (float)(d * 2);
        if ((float)h != 8) return 1;
        return 0;
    }

    static int spotcheck(int i, float f)
    {
        float f2 = h2f((ushort)i);
        if (MathF.Abs((f - f2) / f) > 1e-6)
        {
            Console.Write("error: 0x" + i.ToString("x") + "->" + G(f2, 7) + ", expected ->" + G(f, 7) + ", \n");
            return 1;
        }
        return 0;
    }

    static int spotcheckall()
    {
        float[] t = {
        1.0f, 5.960464e-08f,
        3.0f, 1.788139e-07f,
        8.0f, 4.768372e-07f,
        16.0f, 9.536743e-07f,
        33.0f, 1.966953e-06f,
        83.0f, 4.947186e-06f,
        167.0f, 9.953976e-06f,
        335.0f, 1.996756e-05f,
        838.0f, 4.994869e-05f,
        1677.0f, 9.995699e-05f,
        2701.0f, 0.000199914f,
        4120.0f, 0.0004997253f,
        5144.0f, 0.0009994507f,
        6168.0f, 0.001998901f,
        7454.0f, 0.004997253f,
        8478.0f, 0.009994507f,
        9502.0f, 0.01998901f,
        10854.0f, 0.04998779f,
        11878.0f, 0.09997559f,
        12902.0f, 0.1999512f,
        14336.0f, 0.5f,
        15360.0f, 1.0f,
        16384.0f, 2.0f,
        17664.0f, 5.0f,
        18688.0f, 10.0f,
        19712.0f, 20.0f,
        21056.0f, 50.0f,
        22080.0f, 100.0f,
        23104.0f, 200.0f,
        24528.0f, 500.0f,
        25552.0f, 1000.0f,
        26576.0f, 2000.0f,
        27874.0f, 5000.0f,
        28898.0f, 10000.0f,
        29922.0f, 20000.0f,
        31258.0f, 49984.0f,
        32769.0f, -5.960464e-08f,
        32771.0f, -1.788139e-07f,
        32776.0f, -4.768372e-07f,
        32784.0f, -9.536743e-07f,
        32801.0f, -1.966953e-06f,
        32851.0f, -4.947186e-06f,
        32935.0f, -9.953976e-06f,
        33103.0f, -1.996756e-05f,
        33606.0f, -4.994869e-05f,
        34445.0f, -9.995699e-05f,
        35469.0f, -0.000199914f,
        36888.0f, -0.0004997253f,
        37912.0f, -0.0009994507f,
        38936.0f, -0.001998901f,
        40222.0f, -0.004997253f,
        41246.0f, -0.009994507f,
        42270.0f, -0.01998901f,
        43622.0f, -0.04998779f,
        44646.0f, -0.09997559f,
        45670.0f, -0.1999512f,
        47104.0f, -0.5f,
        48128.0f, -1.0f,
        49152.0f, -2.0f,
        50432.0f, -5.0f,
        51456.0f, -10.0f,
        52480.0f, -20.0f,
        53824.0f, -50.0f,
        54848.0f, -100.0f,
        55872.0f, -200.0f,
        57296.0f, -500.0f,
        58320.0f, -1000.0f,
        59344.0f, -2000.0f,
        60642.0f, -5000.0f,
        61666.0f, -10000.0f,
        62690.0f, -20000.0f,
        64026.0f, -49984.0f,
    };
        int count = 0;
        for (int i = 0; i < t.Length; i += 2)
            count += spotcheck((int)t[i], t[i + 1]);
        return count;
    }

    static int excheck(uint val)
    {
        float f = bitsToFloat(val);
        int i = f2h(f);
        float f2 = h2f((ushort)i);
        if (floatToBits(f) != floatToBits(f2))
        {
            Console.Write("error: " + G(f) + "(0x" + floatToBits(f).ToString("x") + ")->0x" + i.ToString("x") + "->" + G(f2) + "(0x" + floatToBits(f2).ToString("x") + ")\n");
            return 1;
        }
        return 0;
    }

    static int infcheck()
    {
        return excheck(0x7f800000) + excheck(0xff800000);
    }

    static int nancheck()
    {
        int count = 0;
        int nan = 0x7fc00000;
        for (int i = 0; i < (1 << 9); i++)
            if (excheck((uint)(nan | (i << 13))) != 0) count++;
        return count;
    }

    static int overflowtest(float f)
    {
        uint fi = floatToBits(f);
        int i = f2h(f);
        int e = 0x7c00 | (int)((fi >> 16) & 0x8000);
        if (i != e)
        {
            Console.Write("error: " + G(f) + "->0x" + i.ToString("x") + "->" + G(h2f((ushort)i)) + ", expected 0x" + e.ToString("x") + "->" + (((e & 0x8000) != 0) ? "-" : "") + "inf\n");
            return 1;
        }
        return 0;
    }

    static int overflowtestall()
    {
        return overflowtest(65520) + overflowtest(-65520);
    }

    static int test(string name, Func<int> fn)
    {
        Console.Write(name + "...\n");
        int count = fn();
        Console.Write(name + " " + (count != 0 ? "failed" : "passed") + "\n\n");
        return count;
    }

    static int testall(bool fulltest)
    {
        int total = 0;
        total += test("Float/double compatibility", compatcheck);
        total += test("Spot checks", spotcheckall);
        total += test("Bidirectional conversion", testconvertall);
        total += test("Infinity conversion", infcheck);
        total += test("Nan conversion", nancheck);
        total += test("Overflow", overflowtestall);
        total += test("Rounding", fulltest ? testroundall : testroundsome);
        if (total == 0)
            Console.Write("halftest: all tests passed.\n");
        else
            Console.Write("halftest: total errors: " + total + "\n");
        return total;
    }

    public static int Main(string[] args)
    {
        bool fullTest = false;
        return testall(fullTest) != 0 ? 1 : 0;
    }
}
