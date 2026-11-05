// OSL port: the image output buffers of testshade (the C++ has an OIIO::ImageBuf with the requested channel format), shared with testrender

using System;
using OIIO;

namespace OSL.TestShade
{
    /// The output of one -o option: a float image (the C++ has an OIIO::ImageBuf with the requested channel format)
    public sealed unsafe class OutputBuffer
    {
        public string name;
        public string filename;
        public TypeDesc datatype;
        public int nchannels;
        public int xres;
        public int yres;
        public float[] pixels;

        public OutputBuffer(string name, string filename, TypeDesc datatype, int nchannels, int xres, int yres)
        {
            this.name = name;
            this.filename = filename;
            this.datatype = datatype;
            this.nchannels = nchannels;
            this.xres = xres;
            this.yres = yres;
            pixels = new float[xres * yres * nchannels];
        }

        public void setpixel(int x, int y, float* values)
        {
            if (x < 0 || y < 0 || x >= xres || y >= yres)
            {
                return;
            }
            int index = (y * xres + x) * nchannels;
            for (int c = 0; c < nchannels; ++c)
            {
                pixels[index + c] = values[c];
            }
        }
    }
}
