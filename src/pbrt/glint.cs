// (not in pbrt-v4) glint.cs
//
// Glints from a normal map: the "glint" material.
//
//   Material "glint" "string normalmap" "flakes.exr" "float uscale" 8 "float vscale" 8
//       "spectrum eta" "metal-Au-eta" "spectrum k" "metal-Au-k"
//   Material "glint" "string interface" "dielectric" "float eta" 1.5 "string normalmap" "frost.exr"
//
// The surface under a pixel is the normal map itself, taken as a field of mirror facets: every point of the footprint of
// the ray reflects (and, for a dielectric, refracts) as a mirror of the normal that the map has there. What the pixel
// sees is the exact distribution of those normals over the footprint, so that the glints of a small light are found by a
// connection to the light and not by chance. Three sources:
//
//   - the distribution of normals over a footprint (the "P-NDF") is that of Wu, Luan, Hasan, Ramamoorthi, "Position-Normal
//     Manifold for Efficient Glint Rendering on High-Resolution Normal Maps", SIGGRAPH 2025: the map is a mesh of
//     triangles in position and normal, the distribution at a normal is a sum over the triangles whose normals hold it,
//     in closed form and exact, and a hierarchy of the ranges of the normals finds them (PositionNormalManifold). The
//     hierarchy of clusters of the paper, which simplifies the map, is not here;
//   - the microsurface at a point is that of Chermain, Claux, Merillou, "Glint Rendering based on a Multiple-Scattering
//     Patch BRDF", EGSR 2019: the facet of the mapped normal p = (x, y, z) and a facet of the mirrored normal
//     s = (-x, -y, z), of equal areas, so that their mean normal is the normal of the surface and no light is lost below
//     it. With mirror facets the average of that model over a footprint is in closed form (below);
//   - a dielectric transmits, the aim of Zhao, Wang, Wang, Holzschuch, "A discrete microfacet model for transparent glints
//     rendering", CVM 2025. The method here is not theirs: the distribution of the manifold is a distribution of normals,
//     and the refraction is evaluated at the half vector of the refraction as for any rough dielectric.
//
// The model. D_W(m) is the density of the normals of the footprint at m, by area of the unit disc of the normals' x and y
// (the manifold). The two facets of a point have the areas 1 / (2 z) each, so the distribution of the facets is
//     D(m) = (D_W(m) + D_W(mirrored m)) / 2,     with the integral of D(m) cos(theta_m) over the hemisphere 1.
// A facet of the normal m is seen from w, and is not hidden from w by the other facet of its point, with
//     G1(w, m) = min(1, 2 w.z m.z / (<w, m>+ + <w, mirrored m>+)),
// the same at every point whose facet has that normal. One reflection is then
//     f = F(wo . h) D(h) G1(wo, h) G1(wi, h) / (4 cos(theta_o) cos(theta_i))
// and one refraction the same with the half vector and the factors of a rough dielectric: the average over the footprint
// of the local model of Chermain et al. whose facets are mirrors, exactly.
//
// More than one facet. Light that the other facet of a point hides, or that a facet sends below the surface, meets that
// other facet: a walk between the two facets of the sampled point of the footprint, which reflects (or refracts) at each
// until it leaves, as the walk of the microfacet-based normal mapping (microfacetnormalmap.cs). The energy compensation
// of Chermain et al. (a precomputed, approximate lobe) is not used. A walk that leaves after one facet is a sample of
// the closed form above. A walk of two reflections has a closed form as well (GlintBxDF.Two). A longer walk, or one of a
// dielectric that goes through a facet on its way, is a path of mirrors with no closed form for a pair of directions:
// it is returned as a specular sample, found by the sampling of the BSDF only (so a light without size never sees it).
// "integer bounces" is the most facets of a walk (default 16; 1 leaves the first closed form alone and loses the rest).
//
// The footprint is the parallelogram of the pixel on the map, from the differentials that the camera gives for the point
// (pbrt's approximation for a ray without differentials, used for every ray so that the BSDF of a point does not depend
// on the path that came to it), "float footprint" times; its half sides are at least 0.05 texels. A footprint with a half side above "float
// maxfootprint" (32 texels) is cut into equal tiles, and an intersection takes one of them at random: the mean over the
// tiles is the whole footprint, exactly, and the cost is that of one tile.
//
// "float roughness" (0) makes every facet rough: its normal is the mapped normal plus an offset, uniform over a square of
// that half side in the x and y of the normals. The distribution is still in closed form (PositionNormalManifold.Evaluate),
// and a flat piece of the map needs no smallest triangle then.
//
// Polarisation: every facet is a mirror with a known normal, so the Mueller matrix is that of the Fresnel equations at
// the facet, and for a walk the product of its facets.

using System;
using System.Collections.Generic;

namespace pbrt
{
    // GlintFootprint Definition
    // The parallelogram of a ray's footprint on the normal map, in texels: centre, and the two half sides a and b.
    public struct GlintFootprint
    {
        public GlintFootprint(double cx, double cy, double ax, double ay, double bx, double by)
        {
            this.cx = cx;
            this.cy = cy;
            this.ax = ax;
            this.ay = ay;
            this.bx = bx;
            this.by = by;
            double determinant = ax * by - ay * bx;
            i00 = by / determinant;
            i01 = -bx / determinant;
            i10 = -ay / determinant;
            i11 = ax / determinant;
            density = 1 / (4 * Math.Abs(determinant));
        }

        // The density of the footprint at a point of the map, by texel area: uniform over the parallelogram
        public double Kernel(double x, double y)
        {
            double dx = x - cx, dy = y - cy;
            double s = i00 * dx + i01 * dy, t = i10 * dx + i11 * dy;
            return (Math.Abs(s) <= 1 && Math.Abs(t) <= 1) ? density : 0;
        }

        public double cx, cy, ax, ay, bx, by;
        // (the inverse of the two half sides: a point less the centre to its coordinates along them; and the density)
        public double i00, i01, i10, i11, density;
    }

    // PositionNormalManifold Definition
    // The normal map as the mesh of Wu et al.: the texels are the vertices, at whole coordinates, with the x and y of their
    // normals; every cell of four texels is two triangles over which the x and y of the normal are interpolated linearly.
    // The map repeats.
    public sealed class PositionNormalManifold
    {
        // The smallest Jacobian of a triangle (twice its area among the normals): a triangle with less, a flat piece of the
        // map, is given an equilateral triangle of normals of exactly that (Wu et al., section 4.1)
        public const double MinimumJacobian = 1e-6;

        public PositionNormalManifold(Image normalMap)
        {
            width = normalMap.Resolution().x;
            height = normalMap.Resolution().y;
            nx = new float[width * height];
            ny = new float[width * height];
            WrapMode2D wrap = new WrapMode2D(WrapMode.Repeat);
            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    Point2i p = new Point2i(x, y);
                    double vx = 2 * normalMap.GetChannel(p, 0, wrap) - 1, vy = 2 * normalMap.GetChannel(p, 1, wrap) - 1,
                           vz = 2 * normalMap.GetChannel(p, 2, wrap) - 1;
                    double length = Math.Sqrt(vx * vx + vy * vy + vz * vz);
                    if (!(length > 0) || !(vz > 0))
                    {
                        vx = 0;
                        vy = 0;
                        length = 1;
                    }
                    vx /= length;
                    vy /= length;
                    // (a normal at the horizon has no facet that can be seen: held inside the disc)
                    double radius = Math.Sqrt(vx * vx + vy * vy);
                    if (radius > MaximumRadius)
                    {
                        vx *= MaximumRadius / radius;
                        vy *= MaximumRadius / radius;
                    }
                    nx[y * width + x] = (float)vx;
                    ny[y * width + x] = (float)vy;
                }
            }
            BuildHierarchy();
        }

        public int Width
        {
            get { return width; }
        }

        public int Height
        {
            get { return height; }
        }

        // The density of the normals of the footprint at the normal of x and y (mx, my), by area of the disc of the x and y
        // of the normals: the sum, over the triangles that have the normal, of the footprint where they have it over their
        // Jacobian (Wu et al., equation 5)
        //
        // With a roughness r the normal of a point is its mapped normal plus an offset, uniform over the square of the half
        // side r in the x and y of the normals. The density is then the area of the footprint whose mapped normal is within
        // the square of the normal asked for, over the area of the square: over a triangle the mapped normal is linear in
        // the position, so that area is a triangle cut by eight straight lines, in closed form. No triangle is too small
        // then: a flat piece of the map has the square as its distribution.
        public double Evaluate(double mx, double my, GlintFootprint footprint, double roughness)
        {
            // The cells that the parallelogram can reach
            double ex = Math.Abs(footprint.ax) + Math.Abs(footprint.bx), ey = Math.Abs(footprint.ay) + Math.Abs(footprint.by);
            long x0 = (long)Math.Floor(footprint.cx - ex), x1 = (long)Math.Floor(footprint.cx + ex);
            long y0 = (long)Math.Floor(footprint.cy - ey), y1 = (long)Math.Floor(footprint.cy + ey);
            double sum = 0;
            // (by copy of the map: the map repeats)
            for (long tileY = FloorDivide(y0, height); tileY <= FloorDivide(y1, height); ++tileY)
            {
                for (long tileX = FloorDivide(x0, width); tileX <= FloorDivide(x1, width); ++tileX)
                {
                    int left = (int)(Math.Max(x0, tileX * width) - tileX * width), right = (int)(Math.Min(x1, tileX * width + width - 1) - tileX * width);
                    int top = (int)(Math.Max(y0, tileY * height) - tileY * height), bottom = (int)(Math.Min(y1, tileY * height + height - 1) - tileY * height);
                    sum += Descend(levels - 1, 0, 0, left, right, top, bottom, mx, my, footprint, tileX * (double)width, tileY * (double)height,
                                   roughness);
                }
            }
            return sum;
        }

        // The x and y of the normal at a point of the footprint drawn by its density: a normal drawn from Evaluate
        // (with a roughness: plus the offset of `offset` in the square)
        public void Sample(Point2f u, GlintFootprint footprint, double roughness, Point2f offset, out double mx, out double my)
        {
            double s = 2 * (double)u[0] - 1, t = 2 * (double)u[1] - 1;
            double x = footprint.cx + s * footprint.ax + t * footprint.bx, y = footprint.cy + s * footprint.ay + t * footprint.by;
            double floorX = Math.Floor(x), floorY = Math.Floor(y);
            double fx = x - floorX, fy = y - floorY;
            int i = (int)(floorX - Math.Floor(floorX / width) * width), j = (int)(floorY - Math.Floor(floorY / height) * height);
            bool upper = fx + fy >= 1;
            double ax, ay, bx, by, cx, cy;
            if (roughness > 0)
            {
                MappedCorners(i, j, upper, out ax, out ay, out bx, out by, out cx, out cy);
            }
            else
            {
                Corners(i, j, upper, out ax, out ay, out bx, out by, out cx, out cy, out double jacobian);
            }
            // (the weights of the corners: Wu et al., equation 3)
            double l0 = upper ? fx + fy - 1 : 1 - fx - fy, l1 = upper ? 1 - fx : fx, l2 = upper ? 1 - fy : fy;
            mx = l0 * ax + l1 * bx + l2 * cx + roughness * (2 * (double)offset[0] - 1);
            my = l0 * ay + l1 * by + l2 * cy + roughness * (2 * (double)offset[1] - 1);
        }

        private static long FloorDivide(long a, long b)
        {
            long q = a / b;
            return (a % b != 0 && a < 0) ? q - 1 : q;
        }

        // The corners, among the normals, of a triangle of the cell (i, j): the lower one has the texels (i, j), (i + 1, j),
        // (i, j + 1), the upper one (i + 1, j + 1), (i, j + 1), (i + 1, j), in the order of their weights. A triangle that is
        // too small is replaced by the equilateral triangle of the smallest Jacobian around its centre.
        private void Corners(int i, int j, bool upper, out double ax, out double ay, out double bx, out double by, out double cx, out double cy,
                             out double jacobian)
        {
            MappedCorners(i, j, upper, out ax, out ay, out bx, out by, out cx, out cy);
            jacobian = Math.Abs((bx - ax) * (cy - ay) - (by - ay) * (cx - ax));
            if (jacobian < MinimumJacobian)
            {
                double centreX = (ax + bx + cx) / 3, centreY = (ay + by + cy) / 3;
                ax = centreX;
                ay = centreY + EquilateralRadius;
                bx = centreX - EquilateralRadius * 0.8660254037844386;
                by = centreY - EquilateralRadius * 0.5;
                cx = centreX + EquilateralRadius * 0.8660254037844386;
                cy = centreY - EquilateralRadius * 0.5;
                jacobian = MinimumJacobian;
            }
        }

        // The corners as the map has them
        private void MappedCorners(int i, int j, bool upper, out double ax, out double ay, out double bx, out double by, out double cx, out double cy)
        {
            int i1 = i + 1 == width ? 0 : i + 1, j1 = j + 1 == height ? 0 : j + 1;
            int v00 = j * width + i, v10 = j * width + i1, v01 = j1 * width + i, v11 = j1 * width + i1;
            int a = upper ? v11 : v00, b = upper ? v01 : v10, c = upper ? v10 : v01;
            ax = nx[a];
            ay = ny[a];
            bx = nx[b];
            by = ny[b];
            cx = nx[c];
            cy = ny[c];
        }

        // Cuts the polygon (x, y) of `count` corners by the line a x + b y + c = 0, keeping the side where it is positive
        private static int Cut(Span<double> x, Span<double> y, int count, double a, double b, double c, Span<double> outX, Span<double> outY)
        {
            int kept = 0;
            for (int k = 0; k < count; ++k)
            {
                int next = k + 1 == count ? 0 : k + 1;
                double here = a * x[k] + b * y[k] + c, there = a * x[next] + b * y[next] + c;
                if (here >= 0)
                {
                    outX[kept] = x[k];
                    outY[kept] = y[k];
                    ++kept;
                }
                if ((here >= 0) != (there >= 0))
                {
                    double t = here / (here - there);
                    outX[kept] = x[k] + t * (x[next] - x[k]);
                    outY[kept] = y[k] + t * (y[next] - y[k]);
                    ++kept;
                }
            }
            return kept;
        }

        // What one triangle adds to Evaluate with a roughness: the area of the triangle that is in the footprint and has its
        // mapped normal in the square around (mx, my), times the density of the footprint, over the area of the square
        private double RoughTriangle(int i, int j, bool upper, double mx, double my, GlintFootprint footprint, double shiftX, double shiftY,
                                     double roughness)
        {
            MappedCorners(i, j, upper, out double ax, out double ay, out double bx, out double by, out double cx, out double cy);
            // The mapped normal over the cell, by the position (fx, fy) in it: n0 + nfx fx + nfy fy
            double n0x = upper ? bx + cx - ax : ax, n0y = upper ? by + cy - ay : ay;
            double nfxX = upper ? ax - bx : bx - ax, nfxY = upper ? ay - by : by - ay;
            double nfyX = upper ? ax - cx : cx - ax, nfyY = upper ? ay - cy : cy - ay;
            Span<double> x = stackalloc double[12], y = stackalloc double[12], x2 = stackalloc double[12], y2 = stackalloc double[12];
            x[0] = upper ? 1 : 0;
            y[0] = upper ? 1 : 0;
            x[1] = upper ? 0 : 1;
            y[1] = upper ? 1 : 0;
            x[2] = upper ? 1 : 0;
            y[2] = upper ? 0 : 1;
            int count = 3;
            // The square of the normals
            count = Cut(x, y, count, nfxX, nfyX, n0x - mx + roughness, x2, y2);
            if (count < 3) return 0;
            count = Cut(x2, y2, count, -nfxX, -nfyX, mx + roughness - n0x, x, y);
            if (count < 3) return 0;
            count = Cut(x, y, count, nfxY, nfyY, n0y - my + roughness, x2, y2);
            if (count < 3) return 0;
            count = Cut(x2, y2, count, -nfxY, -nfyY, my + roughness - n0y, x, y);
            if (count < 3) return 0;
            // The footprint
            double ox = i + shiftX - footprint.cx, oy = j + shiftY - footprint.cy;
            double s0 = footprint.i00 * ox + footprint.i01 * oy, t0 = footprint.i10 * ox + footprint.i11 * oy;
            count = Cut(x, y, count, -footprint.i00, -footprint.i01, 1 - s0, x2, y2);
            if (count < 3) return 0;
            count = Cut(x2, y2, count, footprint.i00, footprint.i01, 1 + s0, x, y);
            if (count < 3) return 0;
            count = Cut(x, y, count, -footprint.i10, -footprint.i11, 1 - t0, x2, y2);
            if (count < 3) return 0;
            count = Cut(x2, y2, count, footprint.i10, footprint.i11, 1 + t0, x, y);
            if (count < 3) return 0;
            double area = 0;
            for (int k = 0; k < count; ++k)
            {
                int next = k + 1 == count ? 0 : k + 1;
                area += x[k] * y[next] - x[next] * y[k];
            }
            return footprint.density * 0.5 * Math.Abs(area) / (4 * roughness * roughness);
        }

        // What one triangle adds to Evaluate
        private double Triangle(int i, int j, bool upper, double mx, double my, GlintFootprint footprint, double shiftX, double shiftY)
        {
            Corners(i, j, upper, out double ax, out double ay, out double bx, out double by, out double cx, out double cy, out double jacobian);
            double determinant = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
            double l1 = ((mx - ax) * (cy - ay) - (my - ay) * (cx - ax)) / determinant;
            double l2 = ((bx - ax) * (my - ay) - (by - ay) * (mx - ax)) / determinant;
            double l0 = 1 - l1 - l2;
            if (!(l0 >= 0 && l1 >= 0 && l2 >= 0))
            {
                return 0;
            }
            // Where the triangle has the normal
            double fx = upper ? l0 + l2 : l1, fy = upper ? l0 + l1 : l2;
            return footprint.Kernel(i + fx + shiftX, j + fy + shiftY) / jacobian;
        }

        private double Descend(int level, int x, int y, int left, int right, int top, int bottom, double mx, double my, GlintFootprint footprint,
                               double shiftX, double shiftY, double roughness)
        {
            int firstX = x << level, firstY = y << level;
            int lastX = firstX + (1 << level) - 1, lastY = firstY + (1 << level) - 1;
            if (firstX > right || lastX < left || firstY > bottom || lastY < top)
            {
                return 0;
            }
            int index = 4 * (y * levelWidth[level] + x);
            float[] range = ranges[level];
            if (mx + roughness < range[index] || my + roughness < range[index + 1] || mx - roughness > range[index + 2] ||
                my - roughness > range[index + 3])
            {
                return 0;
            }
            if (level == 0)
            {
                if (roughness > 0)
                {
                    return RoughTriangle(x, y, false, mx, my, footprint, shiftX, shiftY, roughness) +
                           RoughTriangle(x, y, true, mx, my, footprint, shiftX, shiftY, roughness);
                }
                return Triangle(x, y, false, mx, my, footprint, shiftX, shiftY) + Triangle(x, y, true, mx, my, footprint, shiftX, shiftY);
            }
            double sum = 0;
            int childWidth = levelWidth[level - 1], childHeight = levelHeight[level - 1];
            for (int dy = 0; dy < 2; ++dy)
            {
                for (int dx = 0; dx < 2; ++dx)
                {
                    int childX = 2 * x + dx, childY = 2 * y + dy;
                    if (childX < childWidth && childY < childHeight)
                    {
                        sum += Descend(level - 1, childX, childY, left, right, top, bottom, mx, my, footprint, shiftX, shiftY, roughness);
                    }
                }
            }
            return sum;
        }

        // The ranges of the normals: of each cell (its two triangles), then of every 2 by 2 block of the level below
        private void BuildHierarchy()
        {
            List<float[]> allRanges = new List<float[]>();
            List<int> widths = new List<int>(), heights = new List<int>();
            float[] cells = new float[4 * width * height];
            for (int j = 0; j < height; ++j)
            {
                for (int i = 0; i < width; ++i)
                {
                    double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                    for (int triangle = 0; triangle < 2; ++triangle)
                    {
                        Corners(i, j, triangle == 1, out double ax, out double ay, out double bx, out double by, out double cx, out double cy,
                                out double jacobian);
                        minX = Math.Min(minX, Math.Min(ax, Math.Min(bx, cx)));
                        minY = Math.Min(minY, Math.Min(ay, Math.Min(by, cy)));
                        maxX = Math.Max(maxX, Math.Max(ax, Math.Max(bx, cx)));
                        maxY = Math.Max(maxY, Math.Max(ay, Math.Max(by, cy)));
                    }
                    int index = 4 * (j * width + i);
                    // (outward, so that the rounding to single precision does not lose a normal at the edge)
                    cells[index] = MathF.BitDecrement((float)minX);
                    cells[index + 1] = MathF.BitDecrement((float)minY);
                    cells[index + 2] = MathF.BitIncrement((float)maxX);
                    cells[index + 3] = MathF.BitIncrement((float)maxY);
                }
            }
            allRanges.Add(cells);
            widths.Add(width);
            heights.Add(height);
            while (widths[widths.Count - 1] > 1 || heights[heights.Count - 1] > 1)
            {
                int belowWidth = widths[widths.Count - 1], belowHeight = heights[heights.Count - 1];
                float[] below = allRanges[allRanges.Count - 1];
                int levelW = (belowWidth + 1) / 2, levelH = (belowHeight + 1) / 2;
                float[] level = new float[4 * levelW * levelH];
                for (int y = 0; y < levelH; ++y)
                {
                    for (int x = 0; x < levelW; ++x)
                    {
                        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                        for (int dy = 0; dy < 2; ++dy)
                        {
                            for (int dx = 0; dx < 2; ++dx)
                            {
                                int childX = 2 * x + dx, childY = 2 * y + dy;
                                if (childX < belowWidth && childY < belowHeight)
                                {
                                    int child = 4 * (childY * belowWidth + childX);
                                    minX = Math.Min(minX, below[child]);
                                    minY = Math.Min(minY, below[child + 1]);
                                    maxX = Math.Max(maxX, below[child + 2]);
                                    maxY = Math.Max(maxY, below[child + 3]);
                                }
                            }
                        }
                        int index = 4 * (y * levelW + x);
                        level[index] = minX;
                        level[index + 1] = minY;
                        level[index + 2] = maxX;
                        level[index + 3] = maxY;
                    }
                }
                allRanges.Add(level);
                widths.Add(levelW);
                heights.Add(levelH);
            }
            ranges = allRanges.ToArray();
            levelWidth = widths.ToArray();
            levelHeight = heights.ToArray();
            levels = ranges.Length;
        }

        // The manifold of a normal map, built once for the materials that share the map
        public static PositionNormalManifold Of(Image normalMap)
        {
            lock (manifolds)
            {
                if (!manifolds.TryGetValue(normalMap, out PositionNormalManifold manifold))
                {
                    manifold = new PositionNormalManifold(normalMap);
                    manifolds.Add(normalMap, manifold);
                }
                return manifold;
            }
        }

        private const double MaximumRadius = 0.999;
        // (the equilateral triangle of the area MinimumJacobian / 2 has this distance from its centre to a corner)
        private static readonly double EquilateralRadius = Math.Sqrt(2 * MinimumJacobian / (3 * Math.Sqrt(3.0)));
        private static readonly Dictionary<Image, PositionNormalManifold> manifolds = new Dictionary<Image, PositionNormalManifold>();

        private readonly int width, height;
        private readonly float[] nx, ny;
        private float[][] ranges;
        private int[] levelWidth, levelHeight;
        private int levels;
    }

    // GlintBxDF Definition
    public sealed unsafe class GlintBxDF : BxDF
    {
        // A conductor of the index eta + i k
        public GlintBxDF(PositionNormalManifold manifold, GlintFootprint footprint, double roughness, SampledSpectrum eta, SampledSpectrum k,
                         int maxBounces)
        {
            this.manifold = manifold;
            this.footprint = footprint;
            this.roughness = roughness;
            this.eta = eta;
            this.k = k;
            this.maxBounces = maxBounces;
            dielectric = false;
        }

        // A dielectric of the index etaBelow, relative to what is above the surface
        public GlintBxDF(PositionNormalManifold manifold, GlintFootprint footprint, double roughness, Float etaBelow, int maxBounces)
        {
            this.manifold = manifold;
            this.footprint = footprint;
            this.roughness = roughness;
            this.etaBelow = etaBelow;
            this.maxBounces = maxBounces;
            dielectric = true;
        }

        public override BxDFFlags Flags()
        {
            BxDFFlags flags = BxDFFlags.GlossyReflection;
            // (a walk of two reflections has a closed form too; a longer one, or one of a dielectric that goes through a facet
            // on its way, is a specular sample)
            if (maxBounces > 2 || (dielectric && maxBounces > 1))
            {
                flags = flags | BxDFFlags.SpecularReflection;
            }
            if (dielectric)
            {
                flags = flags | BxDFFlags.GlossyTransmission;
                if (maxBounces > 1)
                {
                    flags = flags | BxDFFlags.SpecularTransmission;
                }
            }
            return flags;
        }

        public static string Name() { return "GlintBxDF"; }

        public override void Regularize() { }

        public override string ToString()
        {
            return StringPrintf("[ GlintBxDF dielectric: %s eta: %s k: %s etaBelow: %f maxBounces: %d ]", dielectric, eta, k, etaBelow, maxBounces);
        }

        private static Vector3f Mirrored(Vector3f m) { return new Vector3f(-m.x, -m.y, m.z); }

        private static Vector3f FlipZ(Vector3f w) { return new Vector3f(w.x, w.y, -w.z); }

        // The distribution of the facets at the normal m (above the surface)
        private Float D(Vector3f m)
        {
            if (!(m.z > 0) || !(m.x * m.x + m.y * m.y < MaximumRadius * MaximumRadius))
            {
                return 0;
            }
            return (Float)(0.5 * (manifold.Evaluate(m.x, m.y, footprint, roughness) + manifold.Evaluate(-m.x, -m.y, footprint, roughness)));
        }

        // How much of a facet of the normal m the direction w (above the surface) sees past the other facet of its point
        private static Float G1(Vector3f w, Vector3f m)
        {
            if (!(w.z > 0))
            {
                return 0;
            }
            Float projected = std.max<Float>(0, Dot(w, m)) + std.max<Float>(0, Dot(w, Mirrored(m)));
            if (!(projected > 0))
            {
                return 0;
            }
            return std.min<Float>(1, 2 * w.z * m.z / projected);
        }

        // The pair of directions with wo above the surface (the two facets are the same seen from below)
        private bool Orient(ref Vector3f wo, ref Vector3f wi, out Float etaUp)
        {
            etaUp = etaBelow;
            if (wo.z == 0 || wi.z == 0)
            {
                return false;
            }
            if (wo.z < 0)
            {
                wo = FlipZ(wo);
                wi = FlipZ(wi);
                etaUp = 1 / etaBelow;
            }
            return true;
        }

        // One facet, for wo above the surface: the half vector, and the density of wi among the walks that leave after this
        // one facet (but for the choice of a dielectric between reflection and refraction). With the reflectance (or the
        // transmittance) over cos(theta_i) it is the value of f.
        private Float One(Vector3f wo, Vector3f wi, Float etaUp, out bool reflect, out Vector3f wm)
        {
            reflect = wi.z > 0;
            wm = default;
            if (reflect)
            {
                wm = wo + wi;
                if (LengthSquared(wm) == 0)
                {
                    return 0;
                }
                wm = Normalize(wm);
                Float d = D(wm);
                if (!(d > 0))
                {
                    return 0;
                }
                // D G1 G1 / (4 cos(theta_o))
                return d * G1(wo, wm) * G1(wi, wm) / (4 * wo.z);
            }
            if (!dielectric || etaUp == 1)
            {
                return 0;
            }
            wm = wi * etaUp + wo;
            if (LengthSquared(wm) == 0)
            {
                return 0;
            }
            wm = FaceForward(Normalize(wm), new Normal3f(0, 0, 1));
            if (!(Dot(wm, wo) > 0) || !(Dot(wm, wi) < 0))
            {
                return 0;
            }
            Float density = D(wm);
            if (!(density > 0))
            {
                return 0;
            }
            Float denom = Sqr(Dot(wi, wm) + Dot(wo, wm) / etaUp);
            return density * Dot(wo, wm) * G1(wo, wm) * G1(-wi, wm) * AbsDot(wi, wm) / (wo.z * denom);
        }

        // Two facets, both reflections, for wo and wi above the surface: the light meets a facet q, which sends it to the
        // facet of the mirrored normal, which sends it out. Two reflections by q and by its mirror image are a rotation
        // about the level axis across q, by four times the tilt of q: so for a pair of directions the axis is across the
        // level part of wo + wi (either way round), and the tilt is a quarter of the angle that takes -wo to wi about it.
        // For each of the (at most two) facets found: its normal, the cosines of the two reflections, and the density of wi
        // among the walks that leave after these two facets (but for the choices of a dielectric):
        //     D(q) <wo, q> G1(wo, q) / cos(theta_o)      the facet that the light meets first, by its normal
        //     (1 - G1(d, q)), or 1 below the horizon    the reflected light d meets the other facet
        //     G1(wi, mirrored q)                         and leaves from it
        //     / |d wi / d q|                             the solid angle of wi over that of q
        private int Two(Vector3f wo, Vector3f wi, Span<Vector3f> normal, Span<Float> cosineFirst, Span<Float> cosineSecond, Span<Float> density)
        {
            int found = 0;
            Vector3f sum = wo + wi;
            Float level = (Float)Math.Sqrt(sum.x * sum.x + sum.y * sum.y);
            if (!(level > 1e-6f))
            {
                return 0;
            }
            Vector3f v = -wo;
            for (int way = 0; way < 2; ++way)
            {
                Float sign = way == 0 ? 1 : -1;
                // (e: the level direction of the tilt of q; the axis is across it)
                Vector3f e = new Vector3f(sign * sum.x / level, sign * sum.y / level, 0);
                Vector3f axis = new Vector3f(e.y, -e.x, 0);
                double from = Math.Atan2(v.z, Dot(v, e)), to = Math.Atan2(wi.z, Dot(wi, e));
                double angle = to - from;
                angle -= 2 * Math.PI * Math.Floor(angle / (2 * Math.PI));
                double tilt = 0.25 * angle;
                if (!(tilt > 1e-4) || !(tilt < 0.5 * Math.PI - 1e-4))
                {
                    continue;
                }
                Float sine = (Float)Math.Sin(tilt), cosine = (Float)Math.Cos(tilt);
                Vector3f q = new Vector3f(sine * e.x, sine * e.y, cosine);
                if (!(q.x * q.x + q.y * q.y < MaximumRadius * MaximumRadius))
                {
                    continue;
                }
                Vector3f other = Mirrored(q);
                Float cosFirst = Dot(wo, q);
                if (!(cosFirst > 0))
                {
                    continue;
                }
                Vector3f d = v + 2 * cosFirst * q;
                Float cosSecond = -Dot(d, other);
                if (!(cosSecond > 0))
                {
                    continue;
                }
                Vector3f leaves = d + 2 * cosSecond * other;
                if (DistanceSquared((Point3f)leaves, (Point3f)wi) > 1e-6f)
                {
                    continue;
                }
                Float facets = D(q);
                if (!(facets > 0))
                {
                    continue;
                }
                Float goesOn = d.z > 0 ? 1 - G1(d, q) : 1;
                Float visible = facets * cosFirst * G1(wo, q) / wo.z;
                Float chain = visible * goesOn * G1(wi, other);
                if (!(chain > 0))
                {
                    continue;
                }
                // The change of wi with the tilt of q is 4 axis x wi; with the turn of q about the normal of the surface it is
                // z x wi less the rotation of z x (-wo)
                Vector3f up = new Vector3f(0, 0, 1);
                Vector3f byTilt = 4 * Cross(axis, wi);
                Vector3f turned = Cross(up, v);
                Float c4 = (Float)Math.Cos(angle), s4 = (Float)Math.Sin(angle);
                Vector3f rotated = turned * c4 + Cross(axis, turned) * s4 + axis * (Dot(axis, turned) * (1 - c4));
                Vector3f byTurn = Cross(up, wi) - rotated;
                Float jacobian = Math.Abs(Dot(wi, Cross(byTilt, byTurn))) / sine;
                if (!(jacobian > 1e-7f))
                {
                    continue;
                }
                normal[found] = q;
                cosineFirst[found] = cosFirst;
                cosineSecond[found] = cosSecond;
                density[found] = chain / jacobian;
                ++found;
            }
            return found;
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            if (!Orient(ref wo, ref wi, out Float etaUp))
            {
                return new SampledSpectrum(0f);
            }
            SampledSpectrum value = new SampledSpectrum(0f);
            Float common = One(wo, wi, etaUp, out bool reflect, out Vector3f wm);
            if (common > 0)
            {
                if (!dielectric)
                {
                    value = FrComplex(AbsDot(wo, wm), eta, k) * (common / wi.z);
                }
                else
                {
                    Float R = FrDielectric(Dot(wo, wm), etaUp);
                    if (reflect)
                    {
                        value = new SampledSpectrum(R * common / wi.z);
                    }
                    else
                    {
                        Float ft = (1 - R) * common / -wi.z;
                        if (mode == TransportMode.Radiance)
                        {
                            ft /= Sqr(etaUp);
                        }
                        value = new SampledSpectrum(ft);
                    }
                }
            }
            if (reflect && maxBounces >= 2)
            {
                Span<Vector3f> normal = stackalloc Vector3f[2];
                Span<Float> cosineFirst = stackalloc Float[2], cosineSecond = stackalloc Float[2], density = stackalloc Float[2];
                int found = Two(wo, wi, normal, cosineFirst, cosineSecond, density);
                for (int i = 0; i < found; ++i)
                {
                    value += Reflectance(cosineFirst[i], etaUp) * Reflectance(cosineSecond[i], etaUp) * (density[i] / wi.z);
                }
            }
            return value;
        }

        // The reflectance of a facet met at the cosine, from above the surface as the walk has it
        private SampledSpectrum Reflectance(Float cosine, Float etaUp)
        {
            return dielectric ? new SampledSpectrum(FrDielectric(cosine, etaUp)) : FrComplex(cosine, eta, k);
        }

        // The probability that the first facet reflects, of what the flags allow; false if they allow nothing
        private bool FirstReflection(Float R, BxDFReflTransFlags sampleFlags, out Float probability)
        {
            Float pr = (sampleFlags & BxDFReflTransFlags.Reflection) != 0 ? R : 0;
            Float pt = (dielectric && (sampleFlags & BxDFReflTransFlags.Transmission) != 0) ? 1 - R : 0;
            probability = 0;
            if (pr == 0 && pt == 0)
            {
                return false;
            }
            probability = pr / (pr + pt);
            return true;
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if (!Orient(ref wo, ref wi, out Float etaUp))
            {
                return 0;
            }
            Float pdf = 0;
            Float common = One(wo, wi, etaUp, out bool reflect, out Vector3f wm);
            if (common > 0)
            {
                Float R = dielectric ? FrDielectric(Dot(wo, wm), etaUp) : 1;
                if (FirstReflection(R, sampleFlags, out Float probability))
                {
                    pdf = common * (reflect ? probability : 1 - probability);
                }
            }
            if (reflect && maxBounces >= 2)
            {
                Span<Vector3f> normal = stackalloc Vector3f[2];
                Span<Float> cosineFirst = stackalloc Float[2], cosineSecond = stackalloc Float[2], density = stackalloc Float[2];
                int found = Two(wo, wi, normal, cosineFirst, cosineSecond, density);
                for (int i = 0; i < found; ++i)
                {
                    // (a dielectric reflects at both facets with the probability of its reflectance)
                    Float first = dielectric ? FrDielectric(cosineFirst[i], etaUp) : 1;
                    if (FirstReflection(first, sampleFlags, out Float probability))
                    {
                        pdf += density[i] * probability * (dielectric ? FrDielectric(cosineSecond[i], etaUp) : 1);
                    }
                }
            }
            return pdf;
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            Vector3f woGiven = wo;
            walkCount = 0;
            if (wo.z == 0)
            {
                return default;
            }
            // The walk is made above the surface: `side` is -1 while what it has is the mirror image of what is
            Float side = 1, etaUp = etaBelow;
            if (wo.z < 0)
            {
                wo = FlipZ(wo);
                side = -1;
                etaUp = 1 / etaBelow;
            }
            Float firstSide = side, firstEta = etaUp;
            RNG rng = new RNG(Hash(GetOptions().seed, woGiven, uc), Hash(u));

            // A point of the footprint and its two facets; the facet that the walk meets first, by what wo sees of each
            manifold.Sample(u, footprint, roughness, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), out double mx, out double my);
            if (!(mx * mx + my * my < MaximumRadius * MaximumRadius))
            {
                // (a rough facet past the horizon)
                return default;
            }
            Vector3f p = new Vector3f((Float)mx, (Float)my, (Float)Math.Sqrt(Math.Max(0.0, 1 - mx * mx - my * my)));
            Float seenP = std.max<Float>(0, Dot(wo, p)), seenS = std.max<Float>(0, Dot(wo, Mirrored(p)));
            if (!(seenP + seenS > 0))
            {
                return default;
            }
            Vector3f facet = uc * (seenP + seenS) < seenP ? p : Mirrored(p);

            SampledSpectrum weight = new SampledSpectrum(1f);
            bool onlyReflections = true;
            // (the direction in which the light of the walk travels to the facet)
            Vector3f incoming = -wo;
            for (int bounce = 1; bounce <= maxBounces; ++bounce)
            {
                Float cosine = -Dot(incoming, facet);
                if (!(cosine > 0))
                {
                    return default;
                }
                Vector3f outgoing;
                bool reflected = true;
                Float sideBefore = side, etaBefore = etaUp;
                if (!dielectric)
                {
                    if (bounce == 1 && (sampleFlags & BxDFReflTransFlags.Reflection) == 0)
                    {
                        return default;
                    }
                    weight *= FrComplex(cosine, eta, k);
                }
                else
                {
                    Float R = FrDielectric(cosine, etaUp);
                    Float probability = R;
                    if (bounce == 1 && !FirstReflection(R, sampleFlags, out probability))
                    {
                        return default;
                    }
                    reflected = rng.Uniform<Float>() < probability;
                    weight *= reflected ? R / probability : (1 - R) / (1 - probability);
                }
                if (reflected)
                {
                    outgoing = incoming + 2 * cosine * facet;
                }
                else
                {
                    onlyReflections = false;
                    Float etap;
                    Vector3f wt;
                    if (!Refract(-incoming, (Normal3f)facet, etaUp, &etap, &wt))
                    {
                        return default;
                    }
                    if (mode == TransportMode.Radiance)
                    {
                        weight /= Sqr(etaUp);
                    }
                    // Below the facet the walk goes on as above it: the mirror image has the same two facets, the other way round
                    outgoing = FlipZ(wt);
                    side = -side;
                    etaUp = 1 / etaUp;
                }
                Record(new Vector3f(-incoming.x, -incoming.y, -incoming.z * sideBefore),
                       reflected ? new Vector3f(outgoing.x, outgoing.y, outgoing.z * sideBefore) : new Vector3f(outgoing.x, outgoing.y, outgoing.z * side),
                       new Vector3f(facet.x, facet.y, facet.z * sideBefore), cosine, etaBefore, reflected);
                if (!reflected)
                {
                    facet = Mirrored(facet);
                }
                // The light leaves if the other facet does not hide this one from where it goes
                if (outgoing.z > 0 && rng.Uniform<Float>() < G1(outgoing, facet))
                {
                    Vector3f wi = new Vector3f(outgoing.x, outgoing.y, outgoing.z * side);
                    bool transmitted = side != firstSide;
                    if (bounce == 1 || (bounce == 2 && onlyReflections))
                    {
                        // One facet, or two that both reflect: the closed forms
                        walkCount = 0;
                        SampledSpectrum value = f(woGiven, wi, mode);
                        Float pdf = PDF(woGiven, wi, mode, sampleFlags);
                        if (!(bool)value || !(pdf > 0))
                        {
                            return default;
                        }
                        return new BSDFSample(value, wi, pdf, transmitted ? BxDFFlags.GlossyTransmission : BxDFFlags.GlossyReflection,
                                              transmitted ? firstEta : 1);
                    }
                    walkWo = woGiven;
                    walkWi = wi;
                    return new BSDFSample(weight / AbsCosTheta(wi), wi, 1, transmitted ? BxDFFlags.SpecularTransmission : BxDFFlags.SpecularReflection,
                                          transmitted ? firstEta : 1);
                }
                // It meets the other facet
                incoming = outgoing;
                facet = Mirrored(facet);
            }
            walkCount = 0;
            return default;
        }

        // The facets of the last walk that left after more than one, for its Mueller matrix: the direction back to where the
        // walk came from, the direction it left in, the normal on the side it came from, the cosine there, the index of
        // the other side over that of this one, and whether it reflected
        private void Record(Vector3f a, Vector3f b, Vector3f m, Float cosine, Float etaRelative, bool reflected)
        {
            if (walkCount == 0)
            {
                walkA = new Vector3f[maxBounces];
                walkB = new Vector3f[maxBounces];
                walkM = new Vector3f[maxBounces];
                walkCosine = new Float[maxBounces];
                walkEta = new Float[maxBounces];
                walkReflected = new bool[maxBounces];
            }
            walkA[walkCount] = a;
            walkB[walkCount] = b;
            walkM[walkCount] = m;
            walkCosine[walkCount] = cosine;
            walkEta[walkCount] = etaRelative;
            walkReflected[walkCount] = reflected;
            ++walkCount;
        }

        public override bool HasMuellerMatrix() { return true; }

        // The matrix of one facet with a first element of 1, and false if the facet sends no light that way
        private bool Facet(BSDF bsdf, Vector3f a, Vector3f b, Vector3f m, Float cosine, Float etaRelative, bool reflected, bool fromCamera,
                           out PolarisedLightSystem.Attenuation matrix)
        {
            if (!dielectric)
            {
                matrix = PolarisedFacet.Conductor(bsdf, 1, a, b, m, eta, k, fromCamera);
                return PolarisedFacet.Normalise(matrix, FrComplex(cosine, eta, k), new SampledSpectrum(1f), out matrix);
            }
            Float scalar;
            matrix = PolarisedFacet.Dielectric(bsdf, 1, a, b, m, cosine, etaRelative, reflected, fromCamera, out scalar);
            if (!(scalar > 0))
            {
                return false;
            }
            matrix *= new SampledSpectrum(1 / scalar);
            return true;
        }

        public override SampledSpectrum f(BSDF bsdf, Vector3f wo, Vector3f wi, TransportMode mode, out bool polarised,
                                          out PolarisedLightSystem.Attenuation matrix)
        {
            polarised = false;
            matrix = default;
            bool fromCamera = mode == TransportMode.Radiance;
            SampledSpectrum value = f(wo, wi, mode);
            if (walkCount > 1 && DistanceSquared((Point3f)wo, (Point3f)walkWo) < 1e-8f && DistanceSquared((Point3f)wi, (Point3f)walkWi) < 1e-8f)
            {
                // The pair is the walk that Sample_f last returned as a specular sample: the product of its facets
                PolarisedLightSystem.Attenuation chain = default;
                for (int i = 0; i < walkCount; ++i)
                {
                    if (!Facet(bsdf, walkA[i], walkB[i], walkM[i], walkCosine[i], walkEta[i], walkReflected[i], fromCamera,
                               out PolarisedLightSystem.Attenuation next))
                    {
                        return value;
                    }
                    chain = PolarisedFacet.Compose(chain, i > 0, next, fromCamera);
                }
                matrix = chain;
                polarised = !matrix.HasNaNs();
                return value;
            }
            Vector3f woUp = wo, wiUp = wi;
            if (!(bool)value || !Orient(ref woUp, ref wiUp, out Float etaUp))
            {
                return value;
            }
            // The matrices of what f adds up, each by its share of f: one facet, and two
            Float flip = wo.z < 0 ? -1 : 1;
            Vector3f woRender = bsdf.LocalToRender(wo), wiRender = bsdf.LocalToRender(wi);
            Vector3f directionExit = fromCamera ? woRender : wiRender, directionEnter = fromCamera ? -wiRender : -woRender;
            PolarisedLightSystem.Attenuation sum = default;
            bool hasSum = false;
            SampledSpectrum total = new SampledSpectrum(0f);
            Float common = One(woUp, wiUp, etaUp, out bool reflect, out Vector3f wm);
            if (common > 0)
            {
                Float cosine = Dot(woUp, wm);
                SampledSpectrum share = reflect ? Reflectance(cosine, etaUp) * (common / wiUp.z)
                                                : new SampledSpectrum((1 - FrDielectric(cosine, etaUp)) * common / -wiUp.z);
                // (the facet on the side of wo)
                if (Facet(bsdf, wo, wi, new Vector3f(wm.x, wm.y, wm.z * flip), cosine, etaUp, reflect, fromCamera,
                          out PolarisedLightSystem.Attenuation one))
                {
                    Accumulate(ref sum, ref hasSum, ref total, one, share, directionExit, directionEnter);
                }
            }
            if (reflect && maxBounces >= 2)
            {
                Span<Vector3f> normal = stackalloc Vector3f[2];
                Span<Float> cosineFirst = stackalloc Float[2], cosineSecond = stackalloc Float[2], density = stackalloc Float[2];
                int found = Two(woUp, wiUp, normal, cosineFirst, cosineSecond, density);
                for (int i = 0; i < found; ++i)
                {
                    Vector3f q = normal[i];
                    Vector3f between = -woUp + 2 * cosineFirst[i] * q;
                    Vector3f first = new Vector3f(q.x, q.y, q.z * flip), second = new Vector3f(-q.x, -q.y, q.z * flip);
                    Vector3f betweenGiven = new Vector3f(between.x, between.y, between.z * flip);
                    if (Facet(bsdf, wo, betweenGiven, first, cosineFirst[i], etaUp, true, fromCamera, out PolarisedLightSystem.Attenuation a) &&
                        Facet(bsdf, -betweenGiven, wi, second, cosineSecond[i], etaUp, true, fromCamera, out PolarisedLightSystem.Attenuation b))
                    {
                        SampledSpectrum share = Reflectance(cosineFirst[i], etaUp) * Reflectance(cosineSecond[i], etaUp) * (density[i] / wiUp.z);
                        Accumulate(ref sum, ref hasSum, ref total, PolarisedFacet.Compose(a, true, b, fromCamera), share, directionExit, directionEnter);
                    }
                }
            }
            if (hasSum)
            {
                polarised = PolarisedFacet.Normalise(sum, total, new SampledSpectrum(1f), out matrix);
            }
            return value;
        }

        private static void Accumulate(ref PolarisedLightSystem.Attenuation sum, ref bool hasSum, ref SampledSpectrum total,
                                       PolarisedLightSystem.Attenuation matrix, SampledSpectrum share, Vector3f directionExit, Vector3f directionEnter)
        {
            matrix *= share;
            if (!matrix.WithFrames(directionExit, directionEnter) || matrix.HasNaNs())
            {
                return;
            }
            total += share;
            if (hasSum)
            {
                sum += matrix;
            }
            else
            {
                sum = matrix;
                hasSum = true;
            }
        }

        public override pstd.optional<BSDFSample> Sample_f(BSDF bsdf, Vector3f wo, Float uc, Point2f u, TransportMode mode,
                                                           BxDFReflTransFlags sampleFlags, out bool polarised,
                                                           out PolarisedLightSystem.Attenuation matrix)
        {
            polarised = false;
            matrix = default;
            pstd.optional<BSDFSample> bs = Sample_f(wo, uc, u, mode, sampleFlags);
            if (bs)
            {
                f(bsdf, wo, bs.value().wi, mode, out polarised, out matrix);
            }
            return bs;
        }

        private const double MaximumRadius = 0.9995;

        private readonly PositionNormalManifold manifold;
        private readonly GlintFootprint footprint;
        private readonly double roughness;
        private readonly bool dielectric;
        private readonly SampledSpectrum eta, k;
        private readonly Float etaBelow = 1;
        private readonly int maxBounces;
        private int walkCount;
        private Vector3f walkWo, walkWi;
        private Vector3f[] walkA, walkB, walkM;
        private Float[] walkCosine, walkEta;
        private bool[] walkReflected;
    }

    // GlintMaterial Definition
    public sealed unsafe class GlintMaterial : Material
    {
        public GlintMaterial(Image normalMap, bool dielectric, SpectrumTexture eta, SpectrumTexture k, SpectrumTexture reflectance, Spectrum index,
                             Float uScale, Float vScale, Float footprintScale, Float maxFootprint, int maxBounces, FloatTexture displacement,
                             FloatTexture roughness)
        {
            this.roughness = roughness;
            manifold = PositionNormalManifold.Of(normalMap);
            this.dielectric = dielectric;
            this.eta = eta;
            this.k = k;
            this.reflectance = reflectance;
            this.index = index;
            this.uScale = uScale;
            this.vScale = vScale;
            this.footprintScale = footprintScale;
            this.maxFootprint = maxFootprint;
            this.maxBounces = maxBounces;
            this.displacement = displacement;
        }

        public static string Name() { return "GlintMaterial"; }

        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval)
        {
            return dielectric ? texEval.CanEvaluate(new FloatTexture[] { roughness }, new SpectrumTexture[0])
                              : texEval.CanEvaluate(new FloatTexture[] { roughness }, new SpectrumTexture[] { eta, k, reflectance });
        }

        public override FloatTexture GetDisplacement() { return displacement; }

        // (the map is the microsurface of the material, not a change of the shading normal)
        public override Image GetNormalMap() { return null; }

        public override bool HasSubsurfaceScattering() { return false; }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                           ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return null;
        }

        // The smallest half side of a footprint, in texels
        public const double MinimumFootprint = 0.05;

        // The differentials of (u, v) by the pixel, as the camera gives them for the point whatever the ray that came to it
        // (pbrt's approximation for a ray without differentials). The first point of a path from the camera has the
        // differentials of its ray; the same point at the end of a path from a light has the approximation; and the
        // weights of a bidirectional integrator take the two for one BSDF. With a BSDF that depends on the footprint as
        // this one does, that is two BSDFs, and the weights no longer add up. So the footprint is one function of the point.
        private void Differentials(MaterialEvalContext ctx, out double dudx, out double dvdx, out double dudy, out double dvdy)
        {
            dudx = ctx.dudx;
            dvdx = ctx.dvdx;
            dudy = ctx.dudy;
            dvdy = ctx.dvdy;
            SurfaceInteraction si = ctx.si;
            if (si == null || si.differentialsCamera == null || pbrt.GetOptions().disableTextureFiltering)
            {
                return;
            }
            Vector3f dpdx = default, dpdy = default;
            si.differentialsCamera.Approximate_dp_dxy(si.p(), si.n, si.time, si.differentialsSamples, &dpdx, &dpdy);
            // (the least squares solution for the change of (u, v), as SurfaceInteraction.ComputeDifferentials has it)
            double ata00 = pbrt.Dot(si.dpdu, si.dpdu), ata01 = pbrt.Dot(si.dpdu, si.dpdv), ata11 = pbrt.Dot(si.dpdv, si.dpdv);
            double invDet = 1 / (ata00 * ata11 - ata01 * ata01);
            if (double.IsNaN(invDet) || double.IsInfinity(invDet))
            {
                invDet = 0;
            }
            double atb0x = pbrt.Dot(si.dpdu, dpdx), atb1x = pbrt.Dot(si.dpdv, dpdx);
            double atb0y = pbrt.Dot(si.dpdu, dpdy), atb1y = pbrt.Dot(si.dpdv, dpdy);
            dudx = (ata11 * atb0x - ata01 * atb1x) * invDet;
            dvdx = (ata00 * atb1x - ata01 * atb0x) * invDet;
            dudy = (ata11 * atb0y - ata01 * atb1y) * invDet;
            dvdy = (ata00 * atb1y - ata01 * atb0y) * invDet;
        }

        // The footprint of the ray on the map: the pixel is the parallelogram of the two differentials of (u, v), and a texel
        // (i, j) is at u = (i + 0.5) / width, v = 1 - (j + 0.5) / height (as the map is read by pbrt's normal mapping).
        //
        // A footprint with a half side above the largest is cut into equal tiles along its sides, and one tile is taken, by
        // `tileU` (two uniform numbers): the footprint is the mean of its tiles, so a BSDF of one tile drawn at random is,
        // in the mean, the BSDF of the footprint, at the cost of one tile.
        public GlintFootprint Footprint(MaterialEvalContext ctx, Point2f tileU)
        {
            double w = manifold.Width, h = manifold.Height;
            double cx = ctx.uv[0] * uScale * w - 0.5, cy = (1 - ctx.uv[1] * vScale) * h - 0.5;
            Differentials(ctx, out double dudx, out double dvdx, out double dudy, out double dvdy);
            double ax = 0.5 * footprintScale * dudx * uScale * w, ay = -0.5 * footprintScale * dvdx * vScale * h;
            double bx = 0.5 * footprintScale * dudy * uScale * w, by = -0.5 * footprintScale * dvdy * vScale * h;
            double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by);
            if (!(la >= lb))
            {
                double t = ax;
                ax = bx;
                bx = t;
                t = ay;
                ay = by;
                by = t;
                t = la;
                la = lb;
                lb = t;
            }
            // The longer half side, and the other one with what it has across the longer, each at least the smallest
            if (!(la > 0) || double.IsNaN(la) || double.IsInfinity(la))
            {
                ax = MinimumFootprint;
                ay = 0;
                la = MinimumFootprint;
                bx = 0;
                by = 0;
            }
            double scale = Math.Max(la, MinimumFootprint) / la;
            ax *= scale;
            ay *= scale;
            la *= scale;
            double ux = ax / la, uy = ay / la;
            double along = bx * ux + by * uy, across = -bx * uy + by * ux;
            if (double.IsNaN(along) || double.IsInfinity(along) || double.IsNaN(across) || double.IsInfinity(across))
            {
                along = 0;
                across = 0;
            }
            across = (across < 0 ? -1 : 1) * Math.Max(Math.Abs(across), MinimumFootprint);
            bx = along * ux - across * uy;
            by = along * uy + across * ux;
            lb = Math.Sqrt(bx * bx + by * by);
            // The tiles
            int tilesA = (int)Math.Min(Math.Ceiling(la / maxFootprint), 1e6), tilesB = (int)Math.Min(Math.Ceiling(lb / maxFootprint), 1e6);
            if (tilesA > 1 || tilesB > 1)
            {
                int tileA = Math.Min((int)(tileU[0] * tilesA), tilesA - 1), tileB = Math.Min((int)(tileU[1] * tilesB), tilesB - 1);
                double s = -1 + (2 * tileA + 1) / (double)tilesA, t = -1 + (2 * tileB + 1) / (double)tilesB;
                cx += s * ax + t * bx;
                cy += s * ay + t * by;
                ax /= tilesA;
                ay /= tilesA;
                bx /= tilesB;
                by /= tilesB;
            }
            return new GlintFootprint(cx, cy, ax, ay, bx, by);
        }

        public GlintBxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda)
            where TextureEvaluator : ITextureEvaluator
        {
            // (the tile of a large footprint: by the point and the direction, so that the BSDF of an intersection is one BSDF)
            RNG tile = new RNG(pbrt.Hash(ctx.p, ctx.wo), pbrt.Hash(ctx.uv));
            GlintFootprint footprint = Footprint(ctx, new Point2f(tile.Uniform<Float>(), tile.Uniform<Float>()));
            double rough = Math.Min(Math.Max((double)texEval.Apply(roughness, ctx), 0.0), 0.5);
            if (dielectric)
            {
                Float sampledEta = ParametricSpectra.SampleIndex(index, null, texEval, ctx, ref lambda);
                return new GlintBxDF(manifold, footprint, rough, sampledEta, maxBounces);
            }
            SampledSpectrum etas, ks;
            if (eta != null)
            {
                etas = texEval.Apply(eta, ctx, lambda);
                ks = texEval.Apply(k, ctx, lambda);
            }
            else
            {
                SampledSpectrum r = pbrt.Clamp(texEval.Apply(reflectance, ctx, lambda), 0, .9999f);
                etas = new SampledSpectrum(1f);
                ks = 2 * pbrt.Sqrt(r) / pbrt.Sqrt(pbrt.ClampZero(new SampledSpectrum(1f) - r));
            }
            return new GlintBxDF(manifold, footprint, rough, etas, ks, maxBounces);
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx,
                                                       ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(GetBxDF(texEval, ctx, ref lambda)));
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ GlintMaterial dielectric: %s map: %d x %d uScale: %f vScale: %f footprint: %f maxBounces: %d ]", dielectric,
                                     manifold.Width, manifold.Height, uScale, vScale, footprintScale, maxBounces);
        }

        public static GlintMaterial Create(TextureParameterDictionary parameters, Image normalMap, FileLoc loc, Allocator alloc)
        {
            if (normalMap == null)
            {
                pbrt.ErrorExit(loc, "The glint material needs a normal map (\"string normalmap\").");
            }
            string kind = parameters.GetOneString("interface", "conductor");
            if (kind != "conductor" && kind != "dielectric")
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: the interface of the glint material is \"conductor\" or \"dielectric\".", kind));
            }
            bool dielectric = kind == "dielectric";
            SpectrumTexture eta = null, k = null, reflectance = null;
            Spectrum index = null;
            if (dielectric)
            {
                index = ParametricSpectra.Index(parameters, "eta", alloc);
                if (index == null)
                {
                    index = new ConstantSpectrum(1.5f);
                }
            }
            else
            {
                eta = parameters.GetSpectrumTextureOrNull("eta", SpectrumType.Unbounded, alloc);
                k = parameters.GetSpectrumTextureOrNull("k", SpectrumType.Unbounded, alloc);
                reflectance = parameters.GetSpectrumTextureOrNull("reflectance", SpectrumType.Albedo, alloc);
                if (reflectance != null && (eta != null || k != null))
                {
                    pbrt.ErrorExit(loc, "For the glint material, both \"reflectance\" and \"eta\" and \"k\" can't be provided.");
                }
                if (reflectance == null)
                {
                    if (eta == null)
                    {
                        eta = new SpectrumConstantTexture(pbrt.GetNamedSpectrum("metal-Cu-eta"));
                    }
                    if (k == null)
                    {
                        k = new SpectrumConstantTexture(pbrt.GetNamedSpectrum("metal-Cu-k"));
                    }
                }
            }
            Float uScale = parameters.GetOneFloat("uscale", 1f), vScale = parameters.GetOneFloat("vscale", 1f);
            Float footprintScale = parameters.GetOneFloat("footprint", 1f);
            Float maxFootprint = parameters.GetOneFloat("maxfootprint", 32f);
            int maxBounces = parameters.GetOneInt("bounces", 16);
            if (!(footprintScale > 0) || !(maxFootprint >= (Float)MinimumFootprint) || maxBounces < 1 || uScale == 0 || vScale == 0)
            {
                pbrt.ErrorExit(loc, "The glint material needs \"footprint\" above 0, \"maxfootprint\" of 0.05 or more, \"bounces\" of 1 or more, and " +
                                    "\"uscale\" and \"vscale\" that are not 0.");
            }
            FloatTexture displacement = parameters.GetFloatTextureOrNull("displacement", alloc);
            // The roughness of a facet: the half side of the square of its normals (0 to 0.5; 0: a mirror)
            FloatTexture roughness = parameters.GetFloatTexture("roughness", 0f, alloc);
            return new GlintMaterial(normalMap, dielectric, eta, k, reflectance, index, uScale, vScale, footprintScale, maxFootprint, maxBounces,
                                     displacement, roughness);
        }

        private readonly PositionNormalManifold manifold;
        private readonly bool dielectric;
        private readonly SpectrumTexture eta, k, reflectance;
        private readonly Spectrum index;
        private readonly Float uScale, vScale, footprintScale, maxFootprint;
        private readonly int maxBounces;
        private readonly FloatTexture displacement, roughness;
    }
}
