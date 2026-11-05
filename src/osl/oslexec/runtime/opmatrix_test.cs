// (not in OpenShadingLanguage) opmatrix_test.cs
//
// Golden values for the matrix shadeops that need no ShadingContext: each row is a call (name|arguments|results), and the results are what the C++ src/liboslexec/opmatrix.cpp produces, bit for bit.
// Float arguments and results are the hex of the float bits; "b:N" is a 128 float buffer filled from seed N, "w:..." a buffer with these words, "s:text" a
// ustring hash and "n" a null pointer; "k<i>:" lists the first 9 words of argument i when the call changed it. See osl_golden_test.cs.

using testing;
using static testing.Gtest;

namespace OSL
{
    namespace pvt
    {
        public static class OpMatrixGoldenTests
        {
            private static readonly string[] rows = new string[]
            {
                "osl_mul_mmm|b:45786 b:31782 b:60721| k0:3f41d58c,405d974e,c025f3b6,3fd10478,4008caab,c0a3ab24,408e42c7,400d9042,bfeee3c2",
                "osl_mul_mmm|b:277 b:61305 b:62458| k0:3f8d8ab3,3eb978df,3fc3fcd7,3ff37916,3fc84332,c0214e7f,bfc7990a,c00f2483,40595fe2",
                "osl_mul_mmf|b:6705 b:1009 f:bfc04315| k0:402dedea,bfa4f0a6,3f804829,c03b6694,be7fa884,c038d88e,3f5960d4,3ef01d2b,3f39b41f",
                "osl_mul_mmf|b:93217 b:81481 f:3eaa9c81| k0:3ed75558,3e9ac5cf,3e336f40,3e0d326d,3e59c7a1,3ebb59fd,be8d45d8,3f08b3c6,3e18c75c",
                "osl_div_mmm|b:42323 b:61902 b:10932| k0:4181574e,3e5eabb0,409698dc,410addfd,41287752,3e333195,406300a7,40a13250,4115e0ea",
                "osl_div_mmm|b:82520 b:44129 b:59815| k0:3f441604,bee4c54b,3ec9fe58,3e800007,3eb84693,bf2d3ffe,3f0a06c6,3f8b8681,be5caa7d",
                "osl_div_mmf|b:10751 b:80982 f:bec66874| k0:c08b60f9,3fb3f5f9,c09aa6ff,4044bdbf,3d2d53c1,c0a34519,c019992a,4061ae16,c074f61e",
                "osl_div_mmf|b:57712 b:32717 f:c001e401| k0:3ef39fca,3ef6cf8d,bf4aa199,3e8cacae,bf73ae88,bf49cda7,3f6a4a26,beb43ac0,3e588b50",
                "osl_div_mfm|b:10751 f:3f3500aa b:31407| k0:bef62d5f,3de4a087,3f5ad1d1,bf6b69ca,3e5ff054,3c6a87c4,3f0398f9,be24782c,3f36deca",
                "osl_div_mfm|b:57712 f:40156da3 b:32717| k0:3fbfeaf8,3fb923e1,beb8346e,bf6736f0,406e2227,40334abf,3f4e5cc9,3fcc24b1,4052b243",
                "osl_div_m_ff|b:37272 f:bfe3ebbb f:3f7d3350| k0:bfe670dd,00000000,00000000,00000000,00000000,bfe670dd,00000000,00000000,00000000",
                "osl_div_m_ff|b:41911 f:bffb8043 f:c0305429| k0:3f36919e,00000000,00000000,00000000,00000000,3f36919e,00000000,00000000,00000000",
                "osl_transpose_mm|b:9713 b:62277| k0:3e9d8630,3f7de938,3fa87fe8,3fd6bcfa,3e4e8a50,bf15176c,bed2ca90,bfdd0796,bf8a2590",
                "osl_transpose_mm|b:52418 b:78974| k0:bfd41bd4,bea6ebf8,bf475ec4,bf864bd6,bfeeeed2,bfd57b08,bfe6c42a,bf6a1e60,3fcd0398",
                "osl_transform_vmv|b:36793 b:70725 b:65269| k0:bfa873a7,3fa184d8,bfad0e68,bf3cd3b0,bf7e4ffc,3f322c4c,bf663614,bc439c00,3deac060",
                "osl_transform_vmv|b:23351 b:44427 b:81256| k0:3e5b32d3,3f354bd1,3c3daca0,bf48ae24,3d094f00,3e8705a0,3f8e6a0a,bffc22b2,3fa437d4",
                "osl_transform_dvmdv|b:19832 b:16899 b:563| k0:bf7f8fc8,3f407934,3e4189b9,bf2aef26,3f3fabd0,be4ae0c7,4040ebca,3ca39feb,bc5ce14d",
                "osl_transform_dvmdv|b:88571 b:16551 b:86177| k0:bf2abf5a,3f88e040,400a0c53,bf0dbfa2,402e6fa2,409789a2,bf72a92e,bec1b0b3,4014e1e8",
                "osl_transformv_vmv|b:33338 b:70202 b:24857| k0:c01feb70,3ec5ea28,406264c1,bf8cf56a,3ee45578,3e45e940,3e500e30,bffb52e4,3f79bdbc",
                "osl_transformv_vmv|b:33469 b:75253 b:58407| k0:3e85f1f4,c0bf9f14,bfba183f,beb7ed40,bfb68fe6,3f84b0d2,3f21a4e4,bf04c9e8,bfe4004e",
                "osl_transformv_dvmdv|b:84661 b:33885 b:60724| k0:4062c8c2,3d90f540,3e3b60e2,40935045,bfa2a86e,3ffe304b,3fdfa1b0,3f54b745,bf593b46",
                "osl_transformv_dvmdv|b:86633 b:11932 b:25015| k0:bf81255c,c03b1c28,c0651ec4,3f2595ed,3f820be7,40182176,40059fa6,bde81400,bf94e366",
                "osl_transformn_vmv|b:5622 b:94026 b:11539| k0:3f822065,3fa59930,3ffed343,bfe07b86,bda942c0,3fbdecc4,bf11b9f4,3f42feb8,3f234df8",
                "osl_transformn_vmv|b:8021 b:73413 b:18144| k0:bf284c18,bf5ffb18,bd8361e2,bf0ff614,3fd4b4be,be4e2200,3fbc5e98,3fa5e08c,3e6a52a0",
                "osl_transformn_dvmdv|b:2718 b:59576 b:62150| k0:407d1911,3ff3b628,c02667cc,41003d19,404a3da0,c0ab15de,c0c99c66,c03c94d6,40a0049f",
                "osl_transformn_dvmdv|b:71269 b:57784 b:38265| k0:bea01774,3f23a14a,bf702a48,c016fb9e,bc7208c0,3fba6231,3f66f5c8,3f725b4c,bfbff78d",
                "osl_determinant_fm|b:34798| r:c10d681e",
                "osl_determinant_fm|b:21030| r:c1370060",
            };

            [TEST("OpMatrix", "MatchesCxxBitForBit")]
            public static void OpMatrix_MatchesCxxBitForBit()
            {
                string failures = GoldenRuntime.Check(rows);
                EXPECT_TRUE(failures.Length == 0, failures);
            }
        }
    }
}
