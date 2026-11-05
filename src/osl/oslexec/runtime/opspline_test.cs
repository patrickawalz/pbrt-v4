// (not in OpenShadingLanguage) opspline_test.cs
//
// Golden values for spline() and splineinverse(): each row is a call (name|arguments|results), and the results are what the C++ src/liboslexec/opspline.cpp produces, bit for bit.
// Float arguments and results are the hex of the float bits; "b:N" is a 128 float buffer filled from seed N, "w:..." a buffer with these words, "s:text" a
// ustring hash and "n" a null pointer; "k<i>:" lists the first 9 words of argument i when the call changed it. See osl_golden_test.cs.

using testing;
using static testing.Gtest;

namespace OSL
{
    namespace pvt
    {
        public static class OpSplineGoldenTests
        {
            private static readonly string[] rows = new string[]
            {
                "osl_spline_fff|b:91818 s:linear w:3ebd70a4,3d4ccccd,bdcccccd b:96267 i:00000008 i:ffffffff| k0:3deccd88,3ef83450,bd6f2440,bf5263d4,bf7587b0,3f103b78,bf924b76,3f732674,3e82eec0",
                "osl_spline_fff|b:38599 s:constant w:3f11eb85,3d4ccccd,bdcccccd b:93980 i:00000008 i:00000008| k0:3e7e8380,3d957400,bff06836,bf2d10a4,bef7bc50,bfc512ec,3db56880,3fda8d70,bdb83be0",
                "osl_spline_fff|b:6574 s:bogus w:3f451eb8,3d4ccccd,bdcccccd b:76600 i:00000008 i:ffffffff| k0:3efe97f4,3ed5d868,3fd84bcc,3fac9a1e,bf16d87c,bf878e16,3fd80d86,bffed500,bf25f248",
                "osl_spline_dfdfdf|b:42829 s:hermite w:3ebd70a4,3d4ccccd,bdcccccd b:48706 i:00000008 i:ffffffff| k0:bfad6966,3ff227b2,bfbc9e88,3ff84fc2,3fd032b8,bf9a9960,bfb4b156,bf9d4c62,3fa6fdea",
                "osl_spline_dfdfdf|b:28221 s:linear w:3f11eb85,3d4ccccd,bdcccccd b:89391 i:00000008 i:00000008| k0:bd14a608,3f1c6afc,bfc43c22,3f396c8c,3fd59ec6,be0e73f0,bff6eaaa,be2c6b70,bf8e4474",
                "osl_spline_dfdfdf|b:25243 s:constant w:3f451eb8,3d4ccccd,bdcccccd b:49428 i:00000008 i:ffffffff| k0:bf3f11e0,00000000,00000000,3fc87cce,3faa2492,3c8a2b00,bf04e8a4,3fde3c18,3e9d0af0",
                "osl_spline_dffdf|b:91665 s:bezier w:3ebd70a4,3d4ccccd,bdcccccd b:41254 i:00000007 i:ffffffff| k0:3d242f60,bd7061d9,3cc0620c,bffb4c40,bfc2a11e,3fa0ff32,3efd16d0,3ec8cc48,3f1af3fc",
                "osl_spline_dffdf|b:13506 s:bspline w:3f11eb85,3d4ccccd,bdcccccd b:55273 i:00000008 i:00000008| k0:bf2a1cd5,3f94cce4,3f15c08a,bfc32e82,bf84646c,bfcbb13c,3fb5720e,3f83348e,3d184140",
                "osl_spline_dffdf|b:74907 s:hermite w:3f451eb8,3d4ccccd,bdcccccd b:83175 i:00000008 i:ffffffff| k0:be66d96c,bf71b521,bfc3db05,beca42b8,bf90f2ba,3f1b63a0,3fbbda82,be5a5920,3fbd7cb0",
                "osl_spline_dfdff|b:91665 s:bezier w:3ebd70a4,3d4ccccd,bdcccccd b:41254 i:00000007 i:ffffffff| k0:3d242f60,3d1f4270,bd9f4270,bffb4c40,bfc2a11e,3fa0ff32,3efd16d0,3ec8cc48,3f1af3fc",
                "osl_spline_dfdff|b:13506 s:bspline w:3f11eb85,3d4ccccd,bdcccccd b:55273 i:00000008 i:00000008| k0:bf2a1cd5,3e67dcde,bee7dcde,bfc32e82,bf84646c,bfcbb13c,3fb5720e,3f83348e,3d184140",
                "osl_spline_dfdff|b:74907 s:hermite w:3f451eb8,3d4ccccd,bdcccccd b:83175 i:00000008 i:ffffffff| k0:be66d96c,bd608772,3de08772,beca42b8,bf90f2ba,3f1b63a0,3fbbda82,be5a5920,3fbd7cb0",
                "osl_spline_vfv|b:38644 s:bezier w:3ebd70a4,3d4ccccd,bdcccccd b:66188 i:00000007 i:ffffffff| k0:3ea96e44,3da72560,beb8506c,3fe37f6c,3f5b94f4,3fa8506a,bf5d11c0,3f953140,3ff66268",
                "osl_spline_vfv|b:81593 s:bspline w:3f11eb85,3d4ccccd,bdcccccd b:47332 i:00000008 i:00000008| k0:3e9c2e14,3f816e9e,3f5a3310,3fdc2706,3fe0a748,3fa280b4,bf4c3ba8,bebc9de8,bf8ec354",
                "osl_spline_vfv|b:17894 s:hermite w:3f451eb8,3d4ccccd,bdcccccd b:75321 i:00000008 i:ffffffff| k0:3f1cdd2a,be416b14,bef8bd93,bfbf57ae,3c108500,be9b54e0,bfe9b194,3f4eef84,bf6a8930",
                "osl_spline_dvdfv|b:76450 s:constant w:3ebd70a4,3d4ccccd,bdcccccd b:36643 i:00000008 i:ffffffff| k0:bfdcdd08,bf5f5284,3d8578a0,00000000,00000000,00000000,00000000,00000000,00000000",
                "osl_spline_dvdfv|b:55706 s:bogus w:3f11eb85,3d4ccccd,bdcccccd b:73487 i:00000008 i:00000008| k0:bf156512,3f155fd4,3bb0be40,3eae5d0a,bd8a77e0,3dd14650,bf2e5d0a,3e0a77e0,be514650",
                "osl_spline_dvdfv|b:2247 s:catmull-rom w:3f451eb8,3d4ccccd,bdcccccd b:24716 i:00000008 i:ffffffff| k0:beef80a4,3ff0375b,3d946db2,be966c09,bda43e66,bd5eeca8,3f166c09,3e243e66,3ddeeca8",
                "osl_spline_dvfdv|b:76450 s:constant w:3ebd70a4,3d4ccccd,bdcccccd b:36643 i:00000008 i:ffffffff| k0:bfdcdd08,bf5f5284,3d8578a0,00000000,00000000,00000000,00000000,00000000,00000000",
                "osl_spline_dvfdv|b:55706 s:bogus w:3f11eb85,3d4ccccd,bdcccccd b:73487 i:00000008 i:00000008| k0:bf156512,3f155fd4,3bb0be40,3fb9d3e5,3fa7205c,3fa610d6,3f9b4bc6,3faa7218,3fb2eceb",
                "osl_spline_dvfdv|b:2247 s:catmull-rom w:3f451eb8,3d4ccccd,bdcccccd b:24716 i:00000008 i:ffffffff| k0:beef80a4,3ff0375b,3d946db2,3f542e8c,3faf2637,3e2ef0f4,3f4314fe,bf0ee5b9,bf2b20d6",
                "osl_spline_dvdfdv|b:89061 s:catmull-rom w:3ebd70a4,3d4ccccd,bdcccccd b:33863 i:00000008 i:ffffffff| k0:bea9b4b4,bfa36ca4,beecc622,3f443d5c,3f527dce,bef95504,bdfafd28,3fff69f6,3f83c184",
                "osl_spline_dvdfdv|b:20385 s:bezier w:3f11eb85,3d4ccccd,bdcccccd b:72553 i:00000007 i:00000008| k0:bf97728b,3e5106ce,3ef8d612,bfcbbea2,bf986b8c,3f21ce00,bf89246c,40027bc9,3fc11b54",
                "osl_spline_dvdfdv|b:11049 s:bspline w:3f451eb8,3d4ccccd,bdcccccd b:65227 i:00000008 i:ffffffff| k0:3f69ac96,bf80ae04,bfa1fa35,3f2f0a0c,bf5e0816,be7a7d52,bb919200,bf4de0e0,3e869d78",
                "osl_splineinverse_fff|b:41380 s:constant w:3ebd70a4,3d4ccccd,bdcccccd w:00000000,3dcccccd,3e800000,3ecccccd,3f19999a,3f400000,3f666666,3f800000 i:00000008 i:ffffffff| k0:3eccccbc,3ed3c710,bed066f0,3f0aacb4,bfc95738,bff67e62,3ee4d428,3f827f52,bffc8e5c",
                "osl_splineinverse_fff|b:91463 s:bogus w:3f11eb85,3d4ccccd,bdcccccd w:00000000,3dcccccd,3e800000,3ecccccd,3f19999a,3f400000,3f666666,3f800000 i:00000008 i:00000008| k0:3f11eb85,3f998256,3f9925dc,be345bb0,bfa909da,bf1f720c,bfd0dad8,bf0d9cc8,bfee3f76",
                "osl_splineinverse_fff|b:95697 s:catmull-rom w:3f451eb8,3d4ccccd,bdcccccd w:00000000,3dcccccd,3e800000,3ecccccd,3f19999a,3f400000,3f666666,3f800000 i:00000008 i:ffffffff| k0:3f537fea,bfd3d5ce,bf4a214c,3f8769c0,bfa81624,bf121248,3fcaae6a,3fb8554a,3eab4f40",
                "osl_splineinverse_dfdff|b:74576 s:bspline w:3ebd70a4,3d4ccccd,bdcccccd w:00000000,3dcccccd,3e800000,3ecccccd,3f19999a,3f400000,3f666666,3f800000 i:00000008 i:ffffffff| k0:3eb5ae0c,3d784408,bdf84408,bd276dc0,3ff1b2c0,3fdf211a,3fa2b1da,be0dde50,3e2f59c0",
                "osl_splineinverse_dfdff|b:31848 s:hermite w:3f11eb85,3d4ccccd,bdcccccd w:00000000,3dcccccd,3e800000,3ecccccd,3f19999a,3f400000,3f666666,3f800000 i:00000008 i:00000008| k0:3f27163e,3cc87a26,bd487a26,3fd52690,3fe6a3ca,3eaeaca0,be8a9d50,3fe0d27e,3ef7f840",
                "osl_splineinverse_dfdff|b:27823 s:linear w:3f451eb8,3d4ccccd,bdcccccd w:00000000,3dcccccd,3e800000,3ecccccd,3f19999a,3f400000,3f666666,3f800000 i:00000008 i:ffffffff| k0:3f53a06d,3d888888,be088888,bfe31c74,3f6a1054,bea3b5b0,3f645bd8,3faaebe4,3f942310",
                "osl_splineinverse_dfdfdf|b:82022 s:linear w:3ebd70a4,3d4ccccd,bdcccccd w:00000000,3dcccccd,3e800000,3ecccccd,3f19999a,3f400000,3f666666,3f800000 i:00000008 i:ffffffff| k0:3eb851ec,3d888889,be088889,befb91e0,3f9b18bc,3fa444c4,bf8991bc,3eaa2f20,3fe9bda8",
                "osl_splineinverse_dfdfdf|b:28928 s:constant w:3f11eb85,3d4ccccd,bdcccccd w:00000000,3dcccccd,3e800000,3ecccccd,3f19999a,3f400000,3f666666,3f800000 i:00000008 i:00000008| k0:3f199994,36858000,b7058000,bfc3ec7a,3f781ab8,3ee2d910,3fc18740,bf5c10a0,bef4ed78",
                "osl_splineinverse_dfdfdf|b:58789 s:bogus w:3f451eb8,3d4ccccd,bdcccccd w:00000000,3dcccccd,3e800000,3ecccccd,3f19999a,3f400000,3f666666,3f800000 i:00000008 i:ffffffff| k0:3f53a06d,3d888888,be088888,3d8602a0,bf9c11d6,bf474a08,3f6644f0,bda5a460,bfb77e8e",
                "osl_splineinverse_dffdf|b:74576 s:bspline w:3ebd70a4,3d4ccccd,bdcccccd w:00000000,3dcccccd,3e800000,3ecccccd,3f19999a,3f400000,3f666666,3f800000 i:00000008 i:ffffffff| k0:3eb5ae0c,00000000,00000000,bd276dc0,3ff1b2c0,3fdf211a,3fa2b1da,be0dde50,3e2f59c0",
                "osl_splineinverse_dffdf|b:31848 s:hermite w:3f11eb85,3d4ccccd,bdcccccd w:00000000,3dcccccd,3e800000,3ecccccd,3f19999a,3f400000,3f666666,3f800000 i:00000008 i:00000008| k0:3f27163e,00000000,00000000,3fd52690,3fe6a3ca,3eaeaca0,be8a9d50,3fe0d27e,3ef7f840",
                "osl_splineinverse_dffdf|b:27823 s:linear w:3f451eb8,3d4ccccd,bdcccccd w:00000000,3dcccccd,3e800000,3ecccccd,3f19999a,3f400000,3f666666,3f800000 i:00000008 i:ffffffff| k0:3f53a06d,00000000,00000000,bfe31c74,3f6a1054,bea3b5b0,3f645bd8,3faaebe4,3f942310",
            };

            [TEST("OpSpline", "MatchesCxxBitForBit")]
            public static void OpSpline_MatchesCxxBitForBit()
            {
                string failures = GoldenRuntime.Check(rows);
                EXPECT_TRUE(failures.Length == 0, failures);
            }
        }
    }
}
