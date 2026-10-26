// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: MPL-2.0
// nanovdb/util/SampleFromVoxels.h
//
// Port of SampleFromVoxels<TreeOrAccT, 1, false> (tri-linear interpolation without caching
// of the stencil). NOT ported: the Order 0 and 3 samplers, the caching (true) variants,
// gradient() and zeroCrossing().

using System;
using System.Numerics;

namespace nanovdb
{
    public static unsafe class SampleFromVoxelsUtil
    {
        // Floor<CoordT>(Vec3<float>&): returns the Coord of the round-down of xyz and
        // redefines xyz as the fractional part, ie xyz-in = return-value + xyz-out
        public static Coord Floor(ref Vec3<float> xyz)
        {
            float i0 = MathF.Floor(xyz[0]), i1 = MathF.Floor(xyz[1]), i2 = MathF.Floor(xyz[2]);
            xyz[0] -= i0;
            xyz[1] -= i1;
            xyz[2] -= i2;
            return new Coord((int)i0, (int)i1, (int)i2);
        }
        public static Coord Floor(ref Vec3<double> xyz)
        {
            double i0 = Math.Floor(xyz[0]), i1 = Math.Floor(xyz[1]), i2 = Math.Floor(xyz[2]);
            xyz[0] -= i0;
            xyz[1] -= i1;
            xyz[2] -= i2;
            return new Coord((int)i0, (int)i1, (int)i2);
        }
    }

    // SampleFromVoxels<TreeOrAccT, 1, false>; TreeOrAccT is an IVoxelAccess (NanoTree or
    // ReadAccessor), as in the C++.
    public unsafe struct SampleFromVoxels<TreeOrAccT, BuildT>
        where TreeOrAccT : IVoxelAccess<BuildT>
        where BuildT : unmanaged, INumber<BuildT>
    {
        TreeOrAccT mAcc;

        // Construction from a Tree or ReadAccessor
        public SampleFromVoxels(TreeOrAccT acc) { mAcc = acc; }

        // xyz is in index space space
        public BuildT Apply(Vec3<float> xyz)
        {
            Coord coord = SampleFromVoxelsUtil.Floor(ref xyz);
            BuildT vx, vx1, vy, vy1, vz, vz1;

            vz = mAcc.getValue(coord);
            coord[2] += 1;
            vz1 = mAcc.getValue(coord);
            vy = lerp(vz, vz1, xyz[2]);

            coord[1] += 1;

            vz1 = mAcc.getValue(coord);
            coord[2] -= 1;
            vz = mAcc.getValue(coord);
            vy1 = lerp(vz, vz1, xyz[2]);

            vx = lerp(vy, vy1, xyz[1]);

            coord[0] += 1;

            vz = mAcc.getValue(coord);
            coord[2] += 1;
            vz1 = mAcc.getValue(coord);
            vy1 = lerp(vz, vz1, xyz[2]);

            coord[1] -= 1;

            vz1 = mAcc.getValue(coord);
            coord[2] -= 1;
            vz = mAcc.getValue(coord);
            vy = lerp(vz, vz1, xyz[2]);

            vx1 = lerp(vy, vy1, xyz[1]);

            return lerp(vx, vx1, xyz[0]);
        }

        public BuildT Apply(Vec3<double> xyz)
        {
            Coord coord = SampleFromVoxelsUtil.Floor(ref xyz);
            BuildT vx, vx1, vy, vy1, vz, vz1;

            vz = mAcc.getValue(coord);
            coord[2] += 1;
            vz1 = mAcc.getValue(coord);
            vy = lerp(vz, vz1, xyz[2]);

            coord[1] += 1;

            vz1 = mAcc.getValue(coord);
            coord[2] -= 1;
            vz = mAcc.getValue(coord);
            vy1 = lerp(vz, vz1, xyz[2]);

            vx = lerp(vy, vy1, xyz[1]);

            coord[0] += 1;

            vz = mAcc.getValue(coord);
            coord[2] += 1;
            vz1 = mAcc.getValue(coord);
            vy1 = lerp(vz, vz1, xyz[2]);

            coord[1] -= 1;

            vz1 = mAcc.getValue(coord);
            coord[2] -= 1;
            vz = mAcc.getValue(coord);
            vy = lerp(vz, vz1, xyz[2]);

            vx1 = lerp(vy, vy1, xyz[1]);

            return lerp(vx, vx1, xyz[0]);
        }

        // ijk is in index space space
        public BuildT Apply(Coord ijk) { return mAcc.getValue(ijk); }

        // auto lerp = [](ValueT a, ValueT b, RealT w) { return a + ValueT(w) * (b - a); };
        static BuildT lerp<RealT>(BuildT a, BuildT b, RealT w) where RealT : unmanaged, INumber<RealT>
        {
            return a + BuildT.CreateTruncating(w) * (b - a);
        }
    }
}
