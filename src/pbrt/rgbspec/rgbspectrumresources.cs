// (not in pbrt-v4) rgbspectrumresources.cs
//
// Loads the RGB-to-spectrum tables from the files embedded in the assembly (see rgb2spec_opt).

using System;
using System.IO;
using System.Reflection;

namespace pbrt
{
    public static class RgbSpectrumResources
    {
        // count floats of the resource, starting at the float offset
        public static float[] Load(string name, long offset, long count)
        {
            Assembly assembly = typeof(RgbSpectrumResources).Assembly;
            using (Stream stream = assembly.GetManifestResourceStream(name))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("The resource " + name + " is not in the assembly.");
                }
                stream.Seek(offset * sizeof(float), SeekOrigin.Begin);
                float[] result = new float[count];
                byte[] buffer = new byte[1 << 20];
                long done = 0;
                while (done < count)
                {
                    int want = (int)Math.Min(buffer.Length, (count - done) * sizeof(float));
                    int read = 0;
                    while (read < want)
                    {
                        int got = stream.Read(buffer, read, want - read);
                        if (got <= 0)
                        {
                            throw new InvalidOperationException("The resource " + name + " is too short.");
                        }
                        read += got;
                    }
                    Buffer.BlockCopy(buffer, 0, result, (int)(done * sizeof(float)), want);
                    done += want / sizeof(float);
                }
                return result;
            }
        }
    }
}
