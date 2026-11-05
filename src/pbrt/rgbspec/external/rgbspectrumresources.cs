// (not in pbrt-v4) rgbspectrumresources.cs
//
// Loads the RGB-to-spectrum tables from the .bin files beside the assembly; used by rgbspec/external (see rgb2spec_opt, pbrt_lib.csproj).

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
            // The file is beside the assembly (see rgb2spec_opt with "bin", and pbrt_lib.csproj)
            string path = Path.Combine(Path.GetDirectoryName(typeof(RgbSpectrumResources).Assembly.Location), name);
            if (!File.Exists(path))
            {
                throw new InvalidOperationException("The RGB-to-spectrum table file " + path + " is missing.");
            }
            using (Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
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
