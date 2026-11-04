// (not in pbrt-v4) rgbspectrumresources.cs
//
// Loads the RGB-to-spectrum tables from the files embedded in the assembly, or from files next to it (see rgb2spec_opt, pbrt_lib.csproj).

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
            // Embedded in the assembly (the default), or a file next to it (a build with RgbSpecExternal, see pbrt_lib.csproj)
            Stream embedded = assembly.GetManifestResourceStream(name);
            string path = Path.Combine(Path.GetDirectoryName(assembly.Location), name);
            if (embedded == null && !File.Exists(path))
            {
                throw new InvalidOperationException("The RGB-to-spectrum table " + name + " is neither in the assembly nor next to it (" + path + ").");
            }
            using (Stream stream = embedded ?? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
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
