// (not in pbrt-v4) texture_variation_test.cs

using System;
using testing;

namespace pbrt
{
    public static unsafe class VariationTextureTests
    {
        [TEST("Variation", "HsvRoundTripKeepsTheColor")]
        public static void HsvRoundTripKeepsTheColor()
        {
            Random random = new Random(3);
            for (int i = 0; i < 2000; ++i)
            {
                RGB c = new RGB((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());
                RGB back = pbrt.HSVToRGB(pbrt.RGBToHSVShift(c, 0, 1, 1));
                CHECK_LT(Math.Abs(back.r - c.r), 1e-5);
                CHECK_LT(Math.Abs(back.g - c.g), 1e-5);
                CHECK_LT(Math.Abs(back.b - c.b), 1e-5);
            }
        }

        [TEST("Variation", "ShiftsStayInRangeAndAreDeterministic")]
        public static void ShiftsStayInRangeAndAreDeterministic()
        {
            SpectrumVariationTexture texture = new SpectrumVariationTexture(null, new RGB(0.3f, 0.6f, 0.2f), 20, 0.25f, 0.3f, 5, SpectrumType.Albedo, RGBColorSpace.sRGB);
            RGB baseColor = new RGB(0.3f, 0.6f, 0.2f);
            Vector3f reference = pbrt.RGBToHSVShift(baseColor, 0, 1, 1);
            double sumHue = 0, sumValue = 0, worstHue = 0, worstSat = 0, worstVal = 0;
            const int count = 20000;
            for (int id = 1; id <= count; ++id)
            {
                RGB a = texture.Vary(baseColor, id * 7919);
                RGB b = texture.Vary(baseColor, id * 7919);
                CHECK_EQ(a.r, b.r);
                Vector3f hsv = pbrt.RGBToHSVShift(a, 0, 1, 1);
                double dh = hsv.x - reference.x;
                if (dh > 3)
                {
                    dh -= 6;
                }
                if (dh < -3)
                {
                    dh += 6;
                }
                worstHue = Math.Max(worstHue, Math.Abs(dh) * 60);
                worstSat = Math.Max(worstSat, Math.Abs(hsv.y / reference.y - 1));
                worstVal = Math.Max(worstVal, Math.Abs(hsv.z / reference.z - 1));
                sumHue += dh * 60;
                sumValue += hsv.z / reference.z - 1;
            }
            Console.WriteLine("variation: worst hue " + worstHue + " deg, saturation " + worstSat + ", value " + worstVal + "; mean hue " + sumHue / count + " mean value " + sumValue / count);
            CHECK_LT(worstHue, 20.01);
            CHECK_GT(worstHue, 19);
            CHECK_LT(worstSat, 0.2501);
            CHECK_LT(worstVal, 0.3001);
            CHECK_LT(Math.Abs(sumHue / count), 0.4);
            CHECK_LT(Math.Abs(sumValue / count), 0.01);
        }
    }
}
