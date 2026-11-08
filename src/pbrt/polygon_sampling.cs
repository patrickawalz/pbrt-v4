// (not in pbrt-v4) polygon_sampling.cs
//
// Sampling of a convex spherical polygon proportional to its projected solid angle, i.e. proportional to the cosine of the angle to a normal (the z-axis):
// the density of a direction w of the polygon is cos(w, z) / A, where A is the projected solid angle of the polygon. After
//
//   Christoph Peters, BRDF Importance Sampling for Polygonal Lights, ACM Transactions on Graphics 40(4) (SIGGRAPH 2021),
//   and the reference implementation polygon_sampling.glsl of https://github.com/MomentsInGraphics/vulkan_renderer
//
// The polygon is projected to the unit disk; each edge becomes the arc of an ellipse. The polygon is cut into sectors around the zenith, each sector has
// one boundary ellipse (the polygon contains the zenith: the central case, sampled in closed form) or two (the decentral case: the sampled direction is
// found by an iteration of Newton type on the exact area function, started from a good initial guess, which converges cubically). The sampling is exact:
// the iteration solves the exact equation of the inverse of the distribution, and runs here in double precision until the residual is at rounding level
// (at most MaxIterations iterations). The optional approximations of the reference implementation (no iteration, a polynomial atan) are not used.
//
// The input is a convex polygon of unit vectors in the hemisphere z >= 0, wound counterclockwise in the xy-plane when seen from +z (the edge normals
// cross(v[i], v[i + 1]) point to the inside) and without three collinear vertices (on one great circle).
//
// Copyright (C) 2021, Christoph Peters, Karlsruhe Institute of Technology
//
// This source code file is licensed under both the three-clause BSD license and the GPLv3. This port is used under the BSD license:
//
//  Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:
//
//  1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
//
//  2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation
//     and/or other materials provided with the distribution.
//
//  3. Neither the name of the copyright holder nor the names of its contributors may be used to endorse or promote products derived from this software
//     without specific prior written permission.
//
//  THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
//  IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE
//  LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE
//  GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT
//  LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH
//  DAMAGE.

using System;

namespace pbrt
{
    public sealed class ProjectedSolidAnglePolygon
    {
        public const int MaxVertexCount = 8;

        // The iteration of the decentral case: it stops when the residual area is at rounding level relative to the area of the sector
        private const int MaxIterations = 4;
        private const double ConvergedRelativeArea = 4e-16;

        // Vertices closer than this (on the unit sphere) are one vertex
        private const double DuplicateDistance = 1e-12;

        // The ellipse of an edge: the points p of the xy-plane with dot((x, y), p)^2 + dot(p, p) = 1 are the projection of the great circle of the edge. inner tells
        // that the edge runs clockwise (seen from +z) from its first to its second vertex. A degenerate ellipse (the great circle passes through the zenith) has
        // x = infinity and is an outer ellipse.
        private struct Ellipse
        {
            public double x;
            public double y;
            public bool inner;
        }

        // The coefficients of a homogeneous quadratic form a w.x^2 + b w.x w.y + c w.y^2
        private struct Quadratic
        {
            public double a;
            public double b;
            public double c;
        }

        private int vertexCount;
        private readonly double[] vertexX = new double[MaxVertexCount];
        private readonly double[] vertexY = new double[MaxVertexCount];
        private readonly double[] vertexZ = new double[MaxVertexCount];
        private readonly Ellipse[] ellipses = new Ellipse[MaxVertexCount];
        private readonly double[] sectorArea = new double[MaxVertexCount];
        private readonly double[] clipX = new double[MaxVertexCount + 2];
        private readonly double[] clipY = new double[MaxVertexCount + 2];
        private readonly double[] clipZ = new double[MaxVertexCount + 2];
        private Ellipse innerEllipse0;
        private bool central;
        private double projectedSolidAngle;

        // For the tests: Sample() stores the residual of the iteration of the decentral case (in area on the unit disk, and relative to the area of the sector), if
        // recordResidual is set. The area of a thin sector is the difference of two nearly equal areas, so its relative residual is limited by rounding.
        public bool recordResidual;
        public double lastRelativeResidual;
        public double lastAbsoluteResidual;

        // The projected solid angle of the polygon of the last successful Prepare()
        public double ProjectedSolidAngle
        {
            get
            {
                return projectedSolidAngle;
            }
        }

        public bool IsCentral
        {
            get
            {
                return central;
            }
        }

        // The part of the polygon (x, y, z)[0, count) above the plane z = 0 (z * zSign >= 0: zSign = -1 takes the part below it, mirrored to above). The vertices
        // need not be normalized, and may be in either winding. Returns false if nothing is left (no area).
        public bool Prepare(int count, double[] x, double[] y, double[] z, double zSign)
        {
            projectedSolidAngle = 0;
            vertexCount = 0;

            // Clip against the horizon (Sutherland and Hodgman): a convex polygon gets at most one vertex more
            int clipped = 0;
            for (int i = 0; i < count; ++i)
            {
                int j = i + 1 == count ? 0 : i + 1;
                double zi = zSign * z[i];
                double zj = zSign * z[j];
                bool insideI = zi >= 0;
                bool insideJ = zj >= 0;
                if (insideI != insideJ)
                {
                    double t = zi / (zi - zj);
                    clipX[clipped] = x[i] + t * (x[j] - x[i]);
                    clipY[clipped] = y[i] + t * (y[j] - y[i]);
                    clipZ[clipped] = 0;
                    ++clipped;
                }
                if (insideJ)
                {
                    clipX[clipped] = x[j];
                    clipY[clipped] = y[j];
                    clipZ[clipped] = zj;
                    ++clipped;
                }
            }

            // Normalize and drop duplicates
            int n = 0;
            for (int i = 0; i < clipped; ++i)
            {
                double length = Math.Sqrt(clipX[i] * clipX[i] + clipY[i] * clipY[i] + clipZ[i] * clipZ[i]);
                if (length == 0)
                {
                    return false;
                }
                double nx = clipX[i] / length;
                double ny = clipY[i] / length;
                double nz = clipZ[i] / length;
                if (n > 0 && IsDuplicate(vertexX[n - 1], vertexY[n - 1], vertexZ[n - 1], nx, ny, nz))
                {
                    continue;
                }
                vertexX[n] = nx;
                vertexY[n] = ny;
                vertexZ[n] = nz;
                ++n;
            }
            while (n > 1 && IsDuplicate(vertexX[n - 1], vertexY[n - 1], vertexZ[n - 1], vertexX[0], vertexY[0], vertexZ[0]))
            {
                --n;
            }
            if (n < 3)
            {
                return false;
            }
            vertexCount = n;

            // The winding: the edge normals must point to the inside
            double centerX = 0;
            double centerY = 0;
            double centerZ = 0;
            for (int i = 0; i < n; ++i)
            {
                centerX += vertexX[i];
                centerY += vertexY[i];
                centerZ += vertexZ[i];
            }
            double orientation = 0;
            for (int i = 0; i < n; ++i)
            {
                int j = i + 1 == n ? 0 : i + 1;
                double cx = vertexY[i] * vertexZ[j] - vertexZ[i] * vertexY[j];
                double cy = vertexZ[i] * vertexX[j] - vertexX[i] * vertexZ[j];
                double cz = vertexX[i] * vertexY[j] - vertexY[i] * vertexX[j];
                orientation += cx * centerX + cy * centerY + cz * centerZ;
            }
            if (orientation == 0)
            {
                return false;
            }
            if (orientation < 0)
            {
                for (int i = 0, j = n - 1; i < j; ++i, --j)
                {
                    Swap(vertexX, i, j);
                    Swap(vertexY, i, j);
                    Swap(vertexZ, i, j);
                }
            }

            PrepareSectors();
            return projectedSolidAngle > 0 && !double.IsNaN(projectedSolidAngle) && !double.IsInfinity(projectedSolidAngle);
        }

        private static bool IsDuplicate(double ax, double ay, double az, double bx, double by, double bz)
        {
            double dx = ax - bx;
            double dy = ay - by;
            double dz = az - bz;
            return dx * dx + dy * dy + dz * dz <= DuplicateDistance * DuplicateDistance;
        }

        private static void Swap(double[] a, int i, int j)
        {
            double t = a[i];
            a[i] = a[j];
            a[j] = t;
        }

        private static double CrossZ(double ax, double ay, double bx, double by)
        {
            return ax * by - ay * bx;
        }

        // The ellipse of the great circle through two vertices; inner if the edge runs clockwise from the first to the second (seen from +z)
        private static Ellipse EllipseFromEdge(double ax, double ay, double az, double bx, double by, double bz)
        {
            double normalX = ay * bz - az * by;
            double normalY = az * bx - ax * bz;
            double normalZ = CrossZ(ax, ay, bx, by);
            Ellipse ellipse = new Ellipse();
            if (normalZ == 0)
            {
                ellipse.x = double.PositiveInfinity;
                ellipse.y = 0;
                ellipse.inner = false;
                return ellipse;
            }
            ellipse.x = normalX / normalZ;
            ellipse.y = normalY / normalZ;
            ellipse.inner = normalZ < 0;
            return ellipse;
        }

        private static bool IsDegenerate(Ellipse ellipse)
        {
            return double.IsInfinity(ellipse.x);
        }

        private static double EllipseDet(Ellipse ellipse)
        {
            return ellipse.x * ellipse.x + ellipse.y * ellipse.y + 1;
        }

        private static double EllipseRsqrtDet(Ellipse ellipse)
        {
            return 1 / Math.Sqrt(EllipseDet(ellipse));
        }

        // (identity + outer(ellipse, ellipse)) point
        private static void EllipseTransform(Ellipse ellipse, double px, double py, out double rx, out double ry)
        {
            double d = ellipse.x * px + ellipse.y * py;
            rx = px + d * ellipse.x;
            ry = py + d * ellipse.y;
        }

        // The reciprocal square of the factor that moves a direction onto the ellipse
        private static double DirectionFactorRsq(Ellipse ellipse, double dx, double dy)
        {
            double d = ellipse.x * dx + ellipse.y * dy;
            return d * d + dx * dx + dy * dy;
        }

        private static double PositiveAtan(double tangent)
        {
            return Math.Atan(tangent) + (tangent < 0 ? Math.PI : 0.0);
        }

        // Half the area between two ellipses in a sector, from the tangents of the angles of the sector in the metric of each ellipse
        private static double AreaBetweenEllipsesFromTangents(double innerRsqrtDet, double innerTangent, double outerRsqrtDet, double outerTangent)
        {
            double innerArea = innerRsqrtDet * PositiveAtan(innerTangent);
            double result = outerRsqrtDet * PositiveAtan(outerTangent) - innerArea;
            return result > 0 ? 0.5 * result : 0.0;
        }

        private static double AreaBetweenEllipsesInSector(Ellipse inner, double innerRsqrtDet, Ellipse outer, double outerRsqrtDet, double d0x, double d0y,
                                                          double d1x, double d1y)
        {
            double detDirs = Math.Max(0.0, CrossZ(d0x, d0y, d1x, d1y));
            double tx, ty;
            EllipseTransform(inner, d1x, d1y, out tx, out ty);
            double innerDot = innerRsqrtDet * (d0x * tx + d0y * ty);
            EllipseTransform(outer, d1x, d1y, out tx, out ty);
            double outerDot = outerRsqrtDet * (d0x * tx + d0y * ty);
            return AreaBetweenEllipsesFromTangents(innerRsqrtDet, detDirs / innerDot, outerRsqrtDet, detDirs / outerDot);
        }

        private static double EllipseAreaInSector(Ellipse ellipse, double d0x, double d0y, double d1x, double d1y)
        {
            double rsqrtDet = EllipseRsqrtDet(ellipse);
            double detDirs = Math.Max(0.0, CrossZ(d0x, d0y, d1x, d1y));
            double tx, ty;
            EllipseTransform(ellipse, d1x, d1y, out tx, out ty);
            double ellipseDot = rsqrtDet * (d0x * tx + d0y * ty);
            double area = 0.5 * rsqrtDet * PositiveAtan(detDirs / ellipseDot);
            return rsqrtDet > 0 ? area : 0.0;
        }

        // Whether the vertices (and their ellipses) at i and j (i before j) must be exchanged to be in counterclockwise order around the zenith
        private bool MustSwap(int i, int j)
        {
            double crossZ = CrossZ(vertexX[i], vertexY[i], vertexX[j], vertexY[j]);
            if (crossZ == 0)
            {
                // Tie: the vertex with the degenerate ellipse comes first
                return IsDegenerate(ellipses[j]);
            }
            return crossZ < 0;
        }

        private void SwapVertices(int i, int j)
        {
            Swap(vertexX, i, j);
            Swap(vertexY, i, j);
            Swap(vertexZ, i, j);
            Ellipse t = ellipses[i];
            ellipses[i] = ellipses[j];
            ellipses[j] = t;
        }

        private void PrepareSectors()
        {
            int m = vertexCount;

            // The ellipse of each vertex: the ellipse of the edge to the next vertex, unless that edge is an inner edge (the order flips): then the one of the
            // previous edge. innerEllipse0 is the inner ellipse next to vertex 0, if there is one (otherwise the polygon contains the zenith).
            Ellipse first = EllipseFromEdge(vertexX[0], vertexY[0], vertexZ[0], vertexX[1], vertexY[1], vertexZ[1]);
            ellipses[0] = first;
            Ellipse previous = first;
            innerEllipse0 = new Ellipse();
            innerEllipse0.x = 1;
            innerEllipse0.y = 0;
            bool hasInnerEllipse0 = false;
            for (int i = 1; i < m; ++i)
            {
                int next = i + 1 == m ? 0 : i + 1;
                Ellipse ellipse = EllipseFromEdge(vertexX[i], vertexY[i], vertexZ[i], vertexX[next], vertexY[next], vertexZ[next]);
                ellipses[i] = ellipse.inner ? previous : ellipse;
                if (previous.inner && !ellipse.inner)
                {
                    innerEllipse0 = previous;
                    hasInnerEllipse0 = true;
                }
                previous = ellipse;
            }
            // The same for the first vertex: the loop closes
            ellipses[0] = first.inner ? previous : first;
            if (previous.inner && !first.inner)
            {
                innerEllipse0 = previous;
                hasInnerEllipse0 = true;
            }
            central = !hasInnerEllipse0;

            projectedSolidAngle = 0;
            if (central)
            {
                for (int i = 0; i < m; ++i)
                {
                    int next = i + 1 == m ? 0 : i + 1;
                    sectorArea[i] = EllipseAreaInSector(ellipses[i], vertexX[i], vertexY[i], vertexX[next], vertexY[next]);
                    projectedSolidAngle += sectorArea[i];
                }
                return;
            }

            // Counterclockwise around the zenith (insertion sort: the vertices lie in a half plane)
            for (int i = 1; i < m; ++i)
            {
                for (int j = i; j > 0 && MustSwap(j - 1, j); --j)
                {
                    SwapVertices(j - 1, j);
                }
            }

            // m - 1 sectors, each between an inner and an outer ellipse
            Ellipse inner = innerEllipse0;
            double innerRsqrtDet = EllipseRsqrtDet(inner);
            Ellipse outer = ellipses[0];
            double outerRsqrtDet = EllipseRsqrtDet(outer);
            for (int i = 0; i < m - 1; ++i)
            {
                Ellipse vertexEllipse = ellipses[i];
                double vertexRsqrtDet = EllipseRsqrtDet(vertexEllipse);
                if (i == 0)
                {
                    outer = vertexEllipse;
                    outerRsqrtDet = vertexRsqrtDet;
                }
                else if (vertexEllipse.inner)
                {
                    inner = vertexEllipse;
                    innerRsqrtDet = vertexRsqrtDet;
                }
                else
                {
                    outer = vertexEllipse;
                    outerRsqrtDet = vertexRsqrtDet;
                }
                sectorArea[i] = AreaBetweenEllipsesInSector(inner, innerRsqrtDet, outer, outerRsqrtDet, vertexX[i], vertexY[i], vertexX[i + 1], vertexY[i + 1]);
                projectedSolidAngle += sectorArea[i];
            }
        }

        private static double MixFma(double x, double y, double a)
        {
            return Math.FusedMultiplyAdd(a, y, Math.FusedMultiplyAdd(-a, x, x));
        }

        private static void AddOuterProduct(ref Quadratic q, double cx, double cy, double rx, double ry, double scale)
        {
            q.a += scale * cx * rx;
            q.b += scale * (cx * ry + cy * rx);
            q.c += scale * cy * ry;
        }

        // A non-zero root of the quadratic form, as proposed by Blinn (How to Solve a Quadratic Equation, Part 2): stable in all cases
        private static void SolveHomogeneousQuadratic(Quadratic q, out double wx, out double wy)
        {
            double coefficientXY = 0.5 * q.b;
            double discriminant = Math.Max(0.0, coefficientXY * coefficientXY - q.a * q.c);
            double scaledRoot = Math.Abs(coefficientXY) + Math.Sqrt(discriminant);
            if (coefficientXY >= 0)
            {
                wx = scaledRoot;
                wy = -q.a;
            }
            else
            {
                wx = q.c;
                wy = scaledRoot;
            }
        }

        // The area between the two ellipses from direction dir0 to the direction w (all of it in the sector), by the same formula the sectors use
        private static double AreaToDirection(Ellipse inner, double innerRsqrtDet, Ellipse outer, double outerRsqrtDet, double d0x, double d0y, double wx, double wy)
        {
            double detDirs = Math.Max(0.0, CrossZ(d0x, d0y, wx, wy));
            double tx, ty;
            EllipseTransform(inner, wx, wy, out tx, out ty);
            double innerDot = innerRsqrtDet * (d0x * tx + d0y * ty);
            EllipseTransform(outer, wx, wy, out tx, out ty);
            double outerDot = outerRsqrtDet * (d0x * tx + d0y * ty);
            return AreaBetweenEllipsesFromTangents(innerRsqrtDet, detDirs / innerDot, outerRsqrtDet, detDirs / outerDot);
        }

        // A point in the sector between dir0 and dir1 and between the ellipses, uniform in area: xi0 selects the angle (the area up to it is targetArea, which is
        // xi0 times the area of the sector), xi1 the radius.
        private void SampleSectorBetweenEllipses(double xi0, double xi1, double targetArea, double sectorTotalArea, Ellipse inner, Ellipse outer, double dir0x,
                                                 double dir0y, double dir1x, double dir1y, out double sx, out double sy)
        {
            // Initialization: split the sector in half
            double length0 = Math.Sqrt(dir0x * dir0x + dir0y * dir0y);
            double length1 = Math.Sqrt(dir1x * dir1x + dir1y * dir1y);
            double[] qdx = { dir0x / length0, 0, dir1x / length1 };
            double[] qdy = { dir0y / length0, 0, dir1y / length1 };
            qdx[1] = qdx[0] + qdx[2];
            qdy[1] = qdy[0] + qdy[2];

            // Where these lines meet the ellipses: the six points are the corners of two adjacent quads
            double[,] factor = new double[2, 3];
            for (int e = 0; e < 2; ++e)
            {
                Ellipse ellipse = e == 0 ? inner : outer;
                for (int k = 0; k < 3; ++k)
                {
                    double d = ellipse.x * qdx[k] + ellipse.y * qdy[k];
                    double rsq = k == 1 ? d * d + qdx[k] * qdx[k] + qdy[k] * qdy[k] : d * d + 1;
                    factor[e, k] = 1 / Math.Sqrt(rsq);
                }
            }
            double[] quadAreas =
            {
                factor[1, 0] * factor[1, 1] - factor[0, 0] * factor[0, 1],
                factor[1, 1] * factor[1, 2] - factor[0, 1] * factor[0, 2]
            };

            // Pick the quad for the initialization; if it is the first, move its data to the indices 1 and 2
            double targetQuadArea = MixFma(-quadAreas[0], quadAreas[1], xi0);
            if (targetQuadArea <= 0)
            {
                qdx[2] = qdx[0];
                qdy[2] = qdy[0];
                factor[0, 2] = factor[0, 0];
                factor[1, 2] = factor[1, 0];
                targetQuadArea += quadAreas[0];
            }
            else
            {
                targetQuadArea -= quadAreas[1];
            }
            targetQuadArea *= Math.Abs(qdx[1] * qdy[2] - qdx[2] * qdy[1]);

            // The normals of the inner and outer edge of the quad, built like half vectors (by addition) against cancellation
            double normalInnerX = qdx[1] * factor[0, 1] + qdx[2] * factor[0, 2];
            double normalInnerY = qdy[1] * factor[0, 1] + qdy[2] * factor[0, 2];
            double normalOuterX = qdx[1] * factor[1, 1] + qdx[2] * factor[1, 2];
            double normalOuterY = qdy[1] * factor[1, 1] + qdy[2] * factor[1, 2];
            EllipseTransform(inner, normalInnerX, normalInnerY, out normalInnerX, out normalInnerY);
            EllipseTransform(outer, normalOuterX, normalOuterY, out normalOuterX, out normalOuterY);
            double offsetInner = (normalInnerX * qdx[1] + normalInnerY * qdy[1]) * factor[0, 1];
            double offsetOuter = (normalOuterX * qdx[1] + normalOuterY * qdy[1]) * factor[1, 1];

            // The direction in the selected quad: a quadratic equation
            Quadratic quadratic = new Quadratic();
            double rotatedX = -qdy[2];
            double rotatedY = qdx[2];
            AddOuterProduct(ref quadratic, offsetOuter * factor[1, 2] * rotatedX, offsetOuter * factor[1, 2] * rotatedY, normalInnerX, normalInnerY, 1);
            AddOuterProduct(ref quadratic, offsetInner * factor[0, 2] * rotatedX + targetQuadArea * normalInnerX,
                            offsetInner * factor[0, 2] * rotatedY + targetQuadArea * normalInnerY, normalOuterX, normalOuterY, -1);
            double currentX, currentY;
            SolveHomogeneousQuadratic(quadratic, out currentX, out currentY);

            // Refine the initialization (for the boundary values the initialization is perfect and the iteration may be unstable)
            double innerRsqrtDet = EllipseRsqrtDet(inner);
            double outerRsqrtDet = EllipseRsqrtDet(outer);
            const double acceptableError = 1e-12;
            int iterations = Math.Abs(xi0 - 0.5) <= 0.5 - acceptableError ? MaxIterations : 0;
            for (int i = 0; i < iterations; ++i)
            {
                // Avoid under- and overflow, and flip the sign so that the clamping to zero in the area makes sense
                double scaling = 1 / (Math.Abs(currentX) + Math.Abs(currentY));
                scaling = (currentX * qdx[1] + currentY * qdy[1]) >= 0 ? scaling : -scaling;
                currentX *= scaling;
                currentY *= scaling;
                double innerDirX, innerDirY, outerDirX, outerDirY;
                EllipseTransform(inner, currentX, currentY, out innerDirX, out innerDirY);
                EllipseTransform(outer, currentX, currentY, out outerDirX, out outerDirY);
                double detDirs = Math.Max(0.0, CrossZ(qdx[0], qdy[0], currentX, currentY));
                double error = targetArea - AreaBetweenEllipsesFromTangents(innerRsqrtDet, detDirs / (innerRsqrtDet * (qdx[0] * innerDirX + qdy[0] * innerDirY)),
                                                                            outerRsqrtDet, detDirs / (outerRsqrtDet * (qdx[0] * outerDirX + qdy[0] * outerDirY)));
                if (Math.Abs(error) <= ConvergedRelativeArea * sectorTotalArea)
                {
                    break;
                }
                quadratic = new Quadratic();
                AddOuterProduct(ref quadratic, innerDirX - outerDirX, innerDirY - outerDirY, -currentY, currentX, 1);
                AddOuterProduct(ref quadratic, 2 * error * innerDirX, 2 * error * innerDirY, outerDirX, outerDirY, -1);
                SolveHomogeneousQuadratic(quadratic, out currentX, out currentY);
            }
            if (recordResidual)
            {
                // The residual of the direction that is returned
                double scaling = 1 / (Math.Abs(currentX) + Math.Abs(currentY));
                scaling = (currentX * qdx[1] + currentY * qdy[1]) >= 0 ? scaling : -scaling;
                double finalX = currentX * scaling;
                double finalY = currentY * scaling;
                double finalArea = AreaToDirection(inner, innerRsqrtDet, outer, outerRsqrtDet, qdx[0], qdy[0], finalX, finalY);
                lastAbsoluteResidual = Math.Abs(targetArea - finalArea);
                lastRelativeResidual = lastAbsoluteResidual / sectorTotalArea;
            }

            // The halved sector is at most 90 degrees large, so the dot product with the half vector is positive
            if (currentX * qdx[1] + currentY * qdy[1] < 0)
            {
                currentX = -currentX;
                currentY = -currentY;
            }
            // A squared radius uniform between the two ellipses
            double innerFactor = 1 / DirectionFactorRsq(inner, currentX, currentY);
            double outerFactor = 1 / DirectionFactorRsq(outer, currentX, currentY);
            double radius = Math.Sqrt(MixFma(innerFactor, outerFactor, xi1));
            sx = currentX * radius;
            sy = currentY * radius;
        }

        // A direction (unit vector, z >= 0) of the polygon with the density cos(w, z) / ProjectedSolidAngle, for uniform (u0, u1) in [0,1)^2
        public void Sample(double u0, double u1, out double dx, out double dy, out double dz)
        {
            double target = u0 * projectedSolidAngle;
            double sx;
            double sy;
            if (central)
            {
                // A sector, then an angle in it, then a squared radius in the ellipse
                Ellipse outer = ellipses[0];
                double dir0x = vertexX[0];
                double dir0y = vertexY[0];
                for (int i = 0; i < vertexCount; ++i)
                {
                    if (i > 0)
                    {
                        target -= sectorArea[i - 1];
                    }
                    outer = ellipses[i];
                    dir0x = vertexX[i];
                    dir0y = vertexY[i];
                    if ((i >= 2 && i + 1 == vertexCount) || target < sectorArea[i])
                    {
                        break;
                    }
                }
                double sqrtDet = Math.Sqrt(EllipseDet(outer));
                double angle = 2 * target * sqrtDet;
                double tx, ty;
                EllipseTransform(outer, dir0x, dir0y, out tx, out ty);
                double cosine = Math.Cos(angle) * sqrtDet;
                double sine = Math.Sin(angle);
                sx = cosine * dir0x - sine * ty;
                sy = cosine * dir0y + sine * tx;
                double scale = Math.Sqrt(u1 / DirectionFactorRsq(outer, sx, sy));
                sx *= scale;
                sy *= scale;
            }
            else
            {
                Ellipse inner = innerEllipse0;
                Ellipse outer = ellipses[0];
                double dir0x = vertexX[0];
                double dir0y = vertexY[0];
                double dir1x = vertexX[1];
                double dir1y = vertexY[1];
                double chosenArea = sectorArea[0];
                for (int i = 0; i < vertexCount - 1; ++i)
                {
                    Ellipse vertexEllipse = ellipses[i];
                    if (i == 0)
                    {
                        outer = vertexEllipse;
                    }
                    else
                    {
                        target -= sectorArea[i - 1];
                        if (vertexEllipse.inner)
                        {
                            inner = vertexEllipse;
                        }
                        else
                        {
                            outer = vertexEllipse;
                        }
                    }
                    dir0x = vertexX[i];
                    dir0y = vertexY[i];
                    dir1x = vertexX[i + 1];
                    dir1y = vertexY[i + 1];
                    chosenArea = sectorArea[i];
                    if ((i >= 1 && i + 2 == vertexCount) || target < chosenArea)
                    {
                        break;
                    }
                }
                double xi0 = chosenArea > 0 ? Math.Min(target / chosenArea, 1.0) : 0.0;
                SampleSectorBetweenEllipses(xi0, u1, target, chosenArea, inner, outer, dir0x, dir0y, dir1x, dir1y, out sx, out sy);
            }
            dx = sx;
            dy = sy;
            dz = Math.Sqrt(Math.Max(0.0, 1 - sx * sx - sy * sy));
        }
    }
}  // namespace pbrt
