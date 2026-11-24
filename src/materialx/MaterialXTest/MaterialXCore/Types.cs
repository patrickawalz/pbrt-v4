//
// Copyright Contributors to the MaterialX Project
// SPDX-License-Identifier: Apache-2.0
//

using System;
using System.Collections.Generic;
using System.Text;
using MaterialX;
using testing;
using static MaterialX.Mx;
using static MaterialXTest.Catch;

public static class CoreTypesTests
{
    private const float EPSILON = 1e-4f;
    private static readonly float PI = MathF.Acos(-1.0f);

    [TEST("Core", "Vectors")]
    public static void Core_Vectors()
    {
        Vector3 v1 = new Vector3(1, 2, 3);
        Vector3 v2 = new Vector3(2, 4, 6);

        // Indexing operators
        REQUIRE(v1[2] == 3);
        v1[2] = 4;
        REQUIRE(v1[2] == 4);
        v1[2] = 3;

        // Component-wise operators
        REQUIRE(v2 + v1 == new Vector3(3, 6, 9));
        REQUIRE(v2 - v1 == new Vector3(1, 2, 3));
        REQUIRE(v2 * v1 == new Vector3(2, 8, 18));
        REQUIRE(v2 / v1 == new Vector3(2, 2, 2));
        REQUIRE((v2 += v1) == new Vector3(3, 6, 9));
        REQUIRE((v2 -= v1) == new Vector3(2, 4, 6));
        REQUIRE((v2 *= v1) == new Vector3(2, 8, 18));
        REQUIRE((v2 /= v1) == new Vector3(2, 4, 6));
        REQUIRE(v1 * 2 == v2);
        REQUIRE((v1 *= 2) == new Vector3(2, 4, 6));
        REQUIRE((v2 /= 2) == new Vector3(1, 2, 3));

        // Geometric methods
        Vector4 v3 = new Vector4(4);
        REQUIRE(v3.getMagnitude() == 8);
        REQUIRE(v3.getNormalized().getMagnitude() == 1);
        REQUIRE(v1.dot(v2) == 28);
        REQUIRE(v1.cross(v2) == new Vector3());

        // Unary operators
        REQUIRE(-v1 == new Vector3(-2, -4, -6));
        v1 *= -1;
        REQUIRE(+v1 == new Vector3(-2, -4, -6));
    }

    [TEST("Core", "Matrices")]
    public static void Core_Matrices()
    {
        // Translation and scale
        Matrix44 trans = Matrix44.createTranslation(new Vector3(1, 2, 3));
        Matrix44 utrans = Matrix44.createTranslation(new Vector3(1, 2, 3));
        Matrix44 scale = Matrix44.createScale(new Vector3(2));
        REQUIRE(trans == new Matrix44(1, 0, 0, 0,
                                      0, 1, 0, 0,
                                      0, 0, 1, 0,
                                      1, 2, 3, 1));
        REQUIRE(scale == new Matrix44(2, 0, 0, 0,
                                      0, 2, 0, 0,
                                      0, 0, 2, 0,
                                      0, 0, 0, 1));

        // Indexing operators
        REQUIRE(trans[3, 2] == 3);
        trans[3, 2] = 4;
        REQUIRE(trans[3, 2] == 4);
        trans[3, 2] = 3;

        // Matrix methods
        REQUIRE(trans.getTranspose() == new Matrix44(1, 0, 0, 1,
                                                     0, 1, 0, 2,
                                                     0, 0, 1, 3,
                                                     0, 0, 0, 1));
        REQUIRE(scale.getTranspose() == scale);
        REQUIRE(trans.getDeterminant() == 1);
        REQUIRE(scale.getDeterminant() == 8);
        REQUIRE(trans.getInverse() ==
                Matrix44.createTranslation(new Vector3(-1, -2, -3)));

        // Matrix product
        Matrix44 prod1 = trans * scale;
        Matrix44 prod2 = scale * trans;
        Matrix44 prod3 = trans * 2;
        Matrix44 prod4 = trans;
        prod4 *= scale;
        REQUIRE(prod1 == new Matrix44(2, 0, 0, 0,
                                      0, 2, 0, 0,
                                      0, 0, 2, 0,
                                      2, 4, 6, 1));
        REQUIRE(prod2 == new Matrix44(2, 0, 0, 0,
                                      0, 2, 0, 0,
                                      0, 0, 2, 0,
                                      1, 2, 3, 1));
        REQUIRE(prod3 == new Matrix44(2, 0, 0, 0,
                                      0, 2, 0, 0,
                                      0, 0, 2, 0,
                                      2, 4, 6, 2));
        REQUIRE(prod4 == prod1);

        // Matrix division
        Matrix44 quot1 = prod1 / scale;
        Matrix44 quot2 = prod2 / trans;
        Matrix44 quot3 = prod3 / 2;
        Matrix44 quot4 = quot1;
        quot4 /= trans;
        REQUIRE(quot1 == trans);
        REQUIRE(quot2 == scale);
        REQUIRE(quot3 == trans);
        REQUIRE(quot4 == Matrix44.IDENTITY);

        // 2D rotation
        Matrix33 rot1 = Matrix33.createRotation(PI / 2);
        Matrix33 rot2 = Matrix33.createRotation(PI);
        REQUIRE((rot1 * rot1).isEquivalent(rot2, EPSILON));
        REQUIRE(rot2.isEquivalent(Matrix33.createScale(new Vector2(-1)), EPSILON));
        REQUIRE((rot2 * rot2).isEquivalent(Matrix33.IDENTITY, EPSILON));

        // 3D rotation
        Matrix44 rotX = Matrix44.createRotationX(PI);
        Matrix44 rotY = Matrix44.createRotationY(PI);
        Matrix44 rotZ = Matrix44.createRotationZ(PI);
        REQUIRE((rotX * rotY).isEquivalent(Matrix44.createScale(new Vector3(-1, -1, 1)), EPSILON));
        REQUIRE((rotX * rotZ).isEquivalent(Matrix44.createScale(new Vector3(-1, 1, -1)), EPSILON));
        REQUIRE((rotY * rotZ).isEquivalent(Matrix44.createScale(new Vector3(1, -1, -1)), EPSILON));

        // Unary operators
        REQUIRE(-utrans == new Matrix44(-1, 0, 0, 0,
                                        0, -1, 0, 0,
                                        0, 0, -1, 0,
                                        -1, -2, -3, -1));
        utrans*=-1;
        REQUIRE(+utrans == new Matrix44(-1, 0, 0, 0,
                                        0, -1, 0, 0,
                                        0, 0, -1, 0,
                                        -1, -2, -3, -1));
    }
}
