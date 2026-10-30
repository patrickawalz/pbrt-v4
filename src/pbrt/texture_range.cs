// (not in pbrt-v4) Rigorous value ranges of image textures over a region of (s,t) space.
//
// A min/max pyramid over the texels bounds the filtered value of an image texture over any rectangle of texture
// coordinates, including the texels a bilinear lookup reads and the texture's wrap mode. It is what lets a medium
// driven by an image get a tight, unbiased majorant grid, as the sampled-grid media get from their stored maxima.

using System;
using System.Collections.Generic;

namespace pbrt
{
    public sealed unsafe class ImageRangePyramid
    {
        public ImageRangePyramid(Image image)
        {
            Point2i res = image.Resolution();
            resolution = res;
            int nLevels = 1;
            for (int size = std.max(res.x, res.y); size > 1; size = (size + 1) / 2)
                ++nLevels;
            levelWidth = new int[nLevels];
            levelHeight = new int[nLevels];
            levelMin = new Float[nLevels][];
            levelMax = new Float[nLevels][];

            levelWidth[0] = res.x;
            levelHeight[0] = res.y;
            levelMin[0] = new Float[res.x * res.y];
            levelMax[0] = new Float[res.x * res.y];
            for (int y = 0; y < res.y; ++y)
            {
                for (int x = 0; x < res.x; ++x)
                {
                    Float lo = Infinity, hi = -Infinity;
                    for (int c = 0; c < image.NChannels(); ++c)
                    {
                        Float v = image.GetChannel(new Point2i(x, y), c);
                        lo = std.min(lo, v);
                        hi = std.max(hi, v);
                    }
                    levelMin[0][x + res.x * y] = lo;
                    levelMax[0][x + res.x * y] = hi;
                }
            }

            for (int level = 1; level < nLevels; ++level)
            {
                int w = (levelWidth[level - 1] + 1) / 2, h = (levelHeight[level - 1] + 1) / 2;
                levelWidth[level] = w;
                levelHeight[level] = h;
                levelMin[level] = new Float[w * h];
                levelMax[level] = new Float[w * h];
                for (int y = 0; y < h; ++y)
                {
                    for (int x = 0; x < w; ++x)
                    {
                        Float lo = Infinity, hi = -Infinity;
                        for (int dy = 0; dy < 2; ++dy)
                        {
                            for (int dx = 0; dx < 2; ++dx)
                            {
                                int sx = 2 * x + dx, sy = 2 * y + dy;
                                if (sx >= levelWidth[level - 1] || sy >= levelHeight[level - 1])
                                    continue;
                                lo = std.min(lo, levelMin[level - 1][sx + levelWidth[level - 1] * sy]);
                                hi = std.max(hi, levelMax[level - 1][sx + levelWidth[level - 1] * sy]);
                            }
                        }
                        levelMin[level][x + w * y] = lo;
                        levelMax[level][x + w * y] = hi;
                    }
                }
            }
        }

        public Point2i Resolution() { return resolution; }

        // Min and max of the texels in the inclusive rectangle [x0,x1] x [y0,y1], which must lie inside the image.
        // The rectangle is covered by at most 2 x 2 cells of one pyramid level, so the result can be looser than the
        // exact range for large rectangles, but it never excludes a texel inside it.
        public void Query(int x0, int y0, int x1, int y1, ref Float lo, ref Float hi)
        {
            int extent = std.max(x1 - x0, y1 - y0) + 1;
            int level = 0;
            while ((1 << level) < extent && level < levelWidth.Length - 1)
                ++level;
            int cx0 = x0 >> level, cx1 = x1 >> level, cy0 = y0 >> level, cy1 = y1 >> level;
            for (int cy = cy0; cy <= cy1; ++cy)
            {
                for (int cx = cx0; cx <= cx1; ++cx)
                {
                    lo = std.min(lo, levelMin[level][cx + levelWidth[level] * cy]);
                    hi = std.max(hi, levelMax[level][cx + levelWidth[level] * cy]);
                }
            }
        }

        private Point2i resolution;
        private int[] levelWidth, levelHeight;
        private Float[][] levelMin, levelMax;
    }

    public static unsafe partial class pbrt
    {
        // Texel intervals along one axis that a bilinear lookup with coordinates in [s0,s1] can read. Returns false
        // for wrap modes that aren't handled; _includesZero_ is set when the lookup can fall outside a Black image.
        private static bool TexelIntervals(Float s0, Float s1, int n, WrapMode wrap, int[] a, int[] b, out int count,
                                           out bool includesZero)
        {
            count = 0;
            includesZero = false;
            if (!(s0 <= s1) || float.IsInfinity((float)s0) || float.IsInfinity((float)s1))
                return false;
            // The coordinates the lookup rounds to, with one more texel for the interpolation
            double first = std.floor(s0 * n - 0.5), last = std.floor(s1 * n - 0.5) + 1;
            if (Math.Abs(first) > 1e9 || Math.Abs(last) > 1e9)
                return false;
            int i0 = (int)first, i1 = (int)last;
            switch (wrap)
            {
                case WrapMode.Repeat:
                    if (i1 - i0 + 1 >= n)
                    {
                        a[0] = 0; b[0] = n - 1; count = 1;
                    }
                    else
                    {
                        int lo = ((i0 % n) + n) % n, hi = ((i1 % n) + n) % n;
                        if (lo <= hi)
                        {
                            a[0] = lo; b[0] = hi; count = 1;
                        }
                        else
                        {
                            a[0] = lo; b[0] = n - 1;
                            a[1] = 0; b[1] = hi;
                            count = 2;
                        }
                    }
                    return true;
                case WrapMode.Clamp:
                    a[0] = std.min(std.max(i0, 0), n - 1);
                    b[0] = std.min(std.max(i1, 0), n - 1);
                    count = 1;
                    return true;
                case WrapMode.Black:
                    if (i0 < 0 || i1 > n - 1)
                        includesZero = true;
                    if (i1 < 0 || i0 > n - 1)
                        return true;
                    a[0] = std.max(i0, 0);
                    b[0] = std.min(i1, n - 1);
                    count = 1;
                    return true;
                default:
                    return false;
            }
        }

        // Min and max of the (pre-scale) values an image texture with the given wrap mode can return for lookups with
        // texture coordinates in [s0,s1] x [t0,t1], where t is in image row order (0 at the top). Returns false when
        // no bound better than the whole image can be given.
        public static bool ImageRegionRange(ImageRangePyramid pyramid, WrapMode wrap, Float s0, Float s1, Float t0, Float t1,
                                            out Float lo, out Float hi)
        {
            lo = Infinity;
            hi = -Infinity;
            Point2i res = pyramid.Resolution();
            int[] xa = new int[2], xb = new int[2], ya = new int[2], yb = new int[2];
            if (!TexelIntervals(s0, s1, res.x, wrap, xa, xb, out int nx, out bool zeroX) ||
                !TexelIntervals(t0, t1, res.y, wrap, ya, yb, out int ny, out bool zeroY))
                return false;
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < nx; ++i)
                    pyramid.Query(xa[i], ya[j], xb[i], yb[j], ref lo, ref hi);
            if (zeroX || zeroY || nx == 0 || ny == 0)
            {
                lo = std.min(lo, 0);
                hi = std.max(hi, 0);
            }
            return true;
        }
    }
}
