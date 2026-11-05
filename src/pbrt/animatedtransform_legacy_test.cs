// The AnimatedTransform of pbrt-v4 as it was before the N-key generalisation (commit 990cac4, src/pbrt/util/transform.cs),
// renamed. animatedtransform_n_test.cs compares the two-key results of the new AnimatedTransform with it bit for bit.
// The code below is a verbatim copy: do not clean it up.

using System;

namespace pbrt
{

    // AnimatedTransform Definition
    public unsafe class LegacyAnimatedTransform
    {
        // AnimatedTransform Public Methods
        public LegacyAnimatedTransform() { }
        public LegacyAnimatedTransform(Transform t) : this(t, 0, t, 1) { }

        public LegacyAnimatedTransform(Transform startTransform, Float startTime,
                                 Transform endTransform, Float endTime)
        {
            this.startTransform = startTransform;
            this.endTransform = endTransform;
            this.startTime = startTime;
            this.endTime = endTime;
            actuallyAnimated = startTransform != endTransform;
            if (!actuallyAnimated)
                return;
            unsafe
            {
                // Decompose start and end transformations
                SquareMatrix4 Rm = new SquareMatrix4();
                Vector3f T0 = new Vector3f(), T1 = new Vector3f();
                SquareMatrix4 S0 = new SquareMatrix4(), S1 = new SquareMatrix4();
                startTransform.Decompose(&T0, &Rm, &S0);
                T[0] = T0; S[0] = S0;
                R[0] = (Quaternion)new Transform(Rm);

                endTransform.Decompose(&T1, &Rm, &S1);
                T[1] = T1; S[1] = S1;
                R[1] = (Quaternion)new Transform(Rm);
            }

            // Flip _R[1]_ if needed to select shortest path
            if (Dot(R[0], R[1]) < 0)
                R[1] = -R[1];

            hasRotation = Dot(R[0], R[1]) < 0.9995f;

            // Compute terms of motion derivative function
            if (hasRotation)
            {
                Float cosTheta = Dot(R[0], R[1]);
                Float theta = SafeACos(cosTheta);
                Quaternion qperp = Normalize(R[1] - R[0] * cosTheta);

                Float t0x = T[0].x;
                Float t0y = T[0].y;
                Float t0z = T[0].z;
                Float t1x = T[1].x;
                Float t1y = T[1].y;
                Float t1z = T[1].z;
                Float q0x = R[0].v.x;
                Float q0y = R[0].v.y;
                Float q0z = R[0].v.z;
                Float q0w = R[0].w;
                Float qperpx = qperp.v.x;
                Float qperpy = qperp.v.y;
                Float qperpz = qperp.v.z;
                Float qperpw = qperp.w;
                Float s000 = S[0][0][0];
                Float s001 = S[0][0][1];
                Float s002 = S[0][0][2];
                Float s010 = S[0][1][0];
                Float s011 = S[0][1][1];
                Float s012 = S[0][1][2];
                Float s020 = S[0][2][0];
                Float s021 = S[0][2][1];
                Float s022 = S[0][2][2];
                Float s100 = S[1][0][0];
                Float s101 = S[1][0][1];
                Float s102 = S[1][0][2];
                Float s110 = S[1][1][0];
                Float s111 = S[1][1][1];
                Float s112 = S[1][1][2];
                Float s120 = S[1][2][0];
                Float s121 = S[1][2][1];
                Float s122 = S[1][2][2];

            c1[0] = new DerivativeTerm(
                -t0x + t1x,
                (-1 + q0y * q0y + q0z * q0z + qperpy * qperpy + qperpz * qperpz) * s000 +
                    q0w * q0z * s010 - qperpx * qperpy * s010 + qperpw * qperpz * s010 -
                    q0w * q0y * s020 - qperpw * qperpy * s020 - qperpx * qperpz * s020 +
                    s100 - q0y * q0y * s100 - q0z * q0z * s100 - qperpy * qperpy * s100 -
                    qperpz * qperpz * s100 - q0w * q0z * s110 + qperpx * qperpy * s110 -
                    qperpw * qperpz * s110 + q0w * q0y * s120 + qperpw * qperpy * s120 +
                    qperpx * qperpz * s120 +
                    q0x * (-(q0y * s010) - q0z * s020 + q0y * s110 + q0z * s120),
                (-1 + q0y * q0y + q0z * q0z + qperpy * qperpy + qperpz * qperpz) * s001 +
                    q0w * q0z * s011 - qperpx * qperpy * s011 + qperpw * qperpz * s011 -
                    q0w * q0y * s021 - qperpw * qperpy * s021 - qperpx * qperpz * s021 +
                    s101 - q0y * q0y * s101 - q0z * q0z * s101 - qperpy * qperpy * s101 -
                    qperpz * qperpz * s101 - q0w * q0z * s111 + qperpx * qperpy * s111 -
                    qperpw * qperpz * s111 + q0w * q0y * s121 + qperpw * qperpy * s121 +
                    qperpx * qperpz * s121 +
                    q0x * (-(q0y * s011) - q0z * s021 + q0y * s111 + q0z * s121),
                (-1 + q0y * q0y + q0z * q0z + qperpy * qperpy + qperpz * qperpz) * s002 +
                    q0w * q0z * s012 - qperpx * qperpy * s012 + qperpw * qperpz * s012 -
                    q0w * q0y * s022 - qperpw * qperpy * s022 - qperpx * qperpz * s022 +
                    s102 - q0y * q0y * s102 - q0z * q0z * s102 - qperpy * qperpy * s102 -
                    qperpz * qperpz * s102 - q0w * q0z * s112 + qperpx * qperpy * s112 -
                    qperpw * qperpz * s112 + q0w * q0y * s122 + qperpw * qperpy * s122 +
                    qperpx * qperpz * s122 +
                    q0x * (-(q0y * s012) - q0z * s022 + q0y * s112 + q0z * s122));

            c2[0] = new DerivativeTerm(
                0,
                -(qperpy * qperpy * s000) - qperpz * qperpz * s000 + qperpx * qperpy * s010 -
                    qperpw * qperpz * s010 + qperpw * qperpy * s020 + qperpx * qperpz * s020 +
                    q0y * q0y * (s000 - s100) + q0z * q0z * (s000 - s100) +
                    qperpy * qperpy * s100 + qperpz * qperpz * s100 - qperpx * qperpy * s110 +
                    qperpw * qperpz * s110 - qperpw * qperpy * s120 - qperpx * qperpz * s120 +
                    2 * q0x * qperpy * s010 * theta - 2 * q0w * qperpz * s010 * theta +
                    2 * q0w * qperpy * s020 * theta + 2 * q0x * qperpz * s020 * theta +
                    q0y * (q0x * (-s010 + s110) + q0w * (-s020 + s120) +
                           2 * (-2 * qperpy * s000 + qperpx * s010 + qperpw * s020) * theta) +
                    q0z * (q0w * (s010 - s110) + q0x * (-s020 + s120) -
                           2 * (2 * qperpz * s000 + qperpw * s010 - qperpx * s020) * theta),
                -(qperpy * qperpy * s001) - qperpz * qperpz * s001 + qperpx * qperpy * s011 -
                    qperpw * qperpz * s011 + qperpw * qperpy * s021 + qperpx * qperpz * s021 +
                    q0y * q0y * (s001 - s101) + q0z * q0z * (s001 - s101) +
                    qperpy * qperpy * s101 + qperpz * qperpz * s101 - qperpx * qperpy * s111 +
                    qperpw * qperpz * s111 - qperpw * qperpy * s121 - qperpx * qperpz * s121 +
                    2 * q0x * qperpy * s011 * theta - 2 * q0w * qperpz * s011 * theta +
                    2 * q0w * qperpy * s021 * theta + 2 * q0x * qperpz * s021 * theta +
                    q0y * (q0x * (-s011 + s111) + q0w * (-s021 + s121) +
                           2 * (-2 * qperpy * s001 + qperpx * s011 + qperpw * s021) * theta) +
                    q0z * (q0w * (s011 - s111) + q0x * (-s021 + s121) -
                           2 * (2 * qperpz * s001 + qperpw * s011 - qperpx * s021) * theta),
                -(qperpy * qperpy * s002) - qperpz * qperpz * s002 + qperpx * qperpy * s012 -
                    qperpw * qperpz * s012 + qperpw * qperpy * s022 + qperpx * qperpz * s022 +
                    q0y * q0y * (s002 - s102) + q0z * q0z * (s002 - s102) +
                    qperpy * qperpy * s102 + qperpz * qperpz * s102 - qperpx * qperpy * s112 +
                    qperpw * qperpz * s112 - qperpw * qperpy * s122 - qperpx * qperpz * s122 +
                    2 * q0x * qperpy * s012 * theta - 2 * q0w * qperpz * s012 * theta +
                    2 * q0w * qperpy * s022 * theta + 2 * q0x * qperpz * s022 * theta +
                    q0y * (q0x * (-s012 + s112) + q0w * (-s022 + s122) +
                           2 * (-2 * qperpy * s002 + qperpx * s012 + qperpw * s022) * theta) +
                    q0z * (q0w * (s012 - s112) + q0x * (-s022 + s122) -
                           2 * (2 * qperpz * s002 + qperpw * s012 - qperpx * s022) * theta));

            c3[0] = new DerivativeTerm(
                0,
                -2 *
                    (q0x * qperpy * s010 - q0w * qperpz * s010 + q0w * qperpy * s020 +
                     q0x * qperpz * s020 - q0x * qperpy * s110 + q0w * qperpz * s110 -
                     q0w * qperpy * s120 - q0x * qperpz * s120 +
                     q0y * (-2 * qperpy * s000 + qperpx * s010 + qperpw * s020 +
                            2 * qperpy * s100 - qperpx * s110 - qperpw * s120) +
                     q0z * (-2 * qperpz * s000 - qperpw * s010 + qperpx * s020 +
                            2 * qperpz * s100 + qperpw * s110 - qperpx * s120)) *
                    theta,
                -2 *
                    (q0x * qperpy * s011 - q0w * qperpz * s011 + q0w * qperpy * s021 +
                     q0x * qperpz * s021 - q0x * qperpy * s111 + q0w * qperpz * s111 -
                     q0w * qperpy * s121 - q0x * qperpz * s121 +
                     q0y * (-2 * qperpy * s001 + qperpx * s011 + qperpw * s021 +
                            2 * qperpy * s101 - qperpx * s111 - qperpw * s121) +
                     q0z * (-2 * qperpz * s001 - qperpw * s011 + qperpx * s021 +
                            2 * qperpz * s101 + qperpw * s111 - qperpx * s121)) *
                    theta,
                -2 *
                    (q0x * qperpy * s012 - q0w * qperpz * s012 + q0w * qperpy * s022 +
                     q0x * qperpz * s022 - q0x * qperpy * s112 + q0w * qperpz * s112 -
                     q0w * qperpy * s122 - q0x * qperpz * s122 +
                     q0y * (-2 * qperpy * s002 + qperpx * s012 + qperpw * s022 +
                            2 * qperpy * s102 - qperpx * s112 - qperpw * s122) +
                     q0z * (-2 * qperpz * s002 - qperpw * s012 + qperpx * s022 +
                            2 * qperpz * s102 + qperpw * s112 - qperpx * s122)) *
                    theta);

            c4[0] = new DerivativeTerm(
                0,
                -(q0x * qperpy * s010) + q0w * qperpz * s010 - q0w * qperpy * s020 -
                    q0x * qperpz * s020 + q0x * qperpy * s110 - q0w * qperpz * s110 +
                    q0w * qperpy * s120 + q0x * qperpz * s120 + 2 * q0y * q0y * s000 * theta +
                    2 * q0z * q0z * s000 * theta - 2 * qperpy * qperpy * s000 * theta -
                    2 * qperpz * qperpz * s000 * theta + 2 * qperpx * qperpy * s010 * theta -
                    2 * qperpw * qperpz * s010 * theta + 2 * qperpw * qperpy * s020 * theta +
                    2 * qperpx * qperpz * s020 * theta +
                    q0y * (-(qperpx * s010) - qperpw * s020 + 2 * qperpy * (s000 - s100) +
                           qperpx * s110 + qperpw * s120 - 2 * q0x * s010 * theta -
                           2 * q0w * s020 * theta) +
                    q0z * (2 * qperpz * s000 + qperpw * s010 - qperpx * s020 -
                           2 * qperpz * s100 - qperpw * s110 + qperpx * s120 +
                           2 * q0w * s010 * theta - 2 * q0x * s020 * theta),
                -(q0x * qperpy * s011) + q0w * qperpz * s011 - q0w * qperpy * s021 -
                    q0x * qperpz * s021 + q0x * qperpy * s111 - q0w * qperpz * s111 +
                    q0w * qperpy * s121 + q0x * qperpz * s121 + 2 * q0y * q0y * s001 * theta +
                    2 * q0z * q0z * s001 * theta - 2 * qperpy * qperpy * s001 * theta -
                    2 * qperpz * qperpz * s001 * theta + 2 * qperpx * qperpy * s011 * theta -
                    2 * qperpw * qperpz * s011 * theta + 2 * qperpw * qperpy * s021 * theta +
                    2 * qperpx * qperpz * s021 * theta +
                    q0y * (-(qperpx * s011) - qperpw * s021 + 2 * qperpy * (s001 - s101) +
                           qperpx * s111 + qperpw * s121 - 2 * q0x * s011 * theta -
                           2 * q0w * s021 * theta) +
                    q0z * (2 * qperpz * s001 + qperpw * s011 - qperpx * s021 -
                           2 * qperpz * s101 - qperpw * s111 + qperpx * s121 +
                           2 * q0w * s011 * theta - 2 * q0x * s021 * theta),
                -(q0x * qperpy * s012) + q0w * qperpz * s012 - q0w * qperpy * s022 -
                    q0x * qperpz * s022 + q0x * qperpy * s112 - q0w * qperpz * s112 +
                    q0w * qperpy * s122 + q0x * qperpz * s122 + 2 * q0y * q0y * s002 * theta +
                    2 * q0z * q0z * s002 * theta - 2 * qperpy * qperpy * s002 * theta -
                    2 * qperpz * qperpz * s002 * theta + 2 * qperpx * qperpy * s012 * theta -
                    2 * qperpw * qperpz * s012 * theta + 2 * qperpw * qperpy * s022 * theta +
                    2 * qperpx * qperpz * s022 * theta +
                    q0y * (-(qperpx * s012) - qperpw * s022 + 2 * qperpy * (s002 - s102) +
                           qperpx * s112 + qperpw * s122 - 2 * q0x * s012 * theta -
                           2 * q0w * s022 * theta) +
                    q0z * (2 * qperpz * s002 + qperpw * s012 - qperpx * s022 -
                           2 * qperpz * s102 - qperpw * s112 + qperpx * s122 +
                           2 * q0w * s012 * theta - 2 * q0x * s022 * theta));

            c5[0] = new DerivativeTerm(
                0,
                2 *
                    (qperpy * qperpy * s000 + qperpz * qperpz * s000 -
                     qperpx * qperpy * s010 + qperpw * qperpz * s010 -
                     qperpw * qperpy * s020 - qperpx * qperpz * s020 -
                     qperpy * qperpy * s100 - qperpz * qperpz * s100 +
                     q0y * q0y * (-s000 + s100) + q0z * q0z * (-s000 + s100) +
                     qperpx * qperpy * s110 - qperpw * qperpz * s110 +
                     q0y * (q0x * (s010 - s110) + q0w * (s020 - s120)) +
                     qperpw * qperpy * s120 + qperpx * qperpz * s120 +
                     q0z * (-(q0w * s010) + q0x * s020 + q0w * s110 - q0x * s120)) *
                    theta,
                2 *
                    (qperpy * qperpy * s001 + qperpz * qperpz * s001 -
                     qperpx * qperpy * s011 + qperpw * qperpz * s011 -
                     qperpw * qperpy * s021 - qperpx * qperpz * s021 -
                     qperpy * qperpy * s101 - qperpz * qperpz * s101 +
                     q0y * q0y * (-s001 + s101) + q0z * q0z * (-s001 + s101) +
                     qperpx * qperpy * s111 - qperpw * qperpz * s111 +
                     q0y * (q0x * (s011 - s111) + q0w * (s021 - s121)) +
                     qperpw * qperpy * s121 + qperpx * qperpz * s121 +
                     q0z * (-(q0w * s011) + q0x * s021 + q0w * s111 - q0x * s121)) *
                    theta,
                2 *
                    (qperpy * qperpy * s002 + qperpz * qperpz * s002 -
                     qperpx * qperpy * s012 + qperpw * qperpz * s012 -
                     qperpw * qperpy * s022 - qperpx * qperpz * s022 -
                     qperpy * qperpy * s102 - qperpz * qperpz * s102 +
                     q0y * q0y * (-s002 + s102) + q0z * q0z * (-s002 + s102) +
                     qperpx * qperpy * s112 - qperpw * qperpz * s112 +
                     q0y * (q0x * (s012 - s112) + q0w * (s022 - s122)) +
                     qperpw * qperpy * s122 + qperpx * qperpz * s122 +
                     q0z * (-(q0w * s012) + q0x * s022 + q0w * s112 - q0x * s122)) *
                    theta);

            c1[1] = new DerivativeTerm(
                -t0y + t1y,
                -(qperpx * qperpy * s000) - qperpw * qperpz * s000 - s010 + q0z * q0z * s010 +
                    qperpx * qperpx * s010 + qperpz * qperpz * s010 - q0y * q0z * s020 +
                    qperpw * qperpx * s020 - qperpy * qperpz * s020 + qperpx * qperpy * s100 +
                    qperpw * qperpz * s100 + q0w * q0z * (-s000 + s100) +
                    q0x * q0x * (s010 - s110) + s110 - q0z * q0z * s110 -
                    qperpx * qperpx * s110 - qperpz * qperpz * s110 +
                    q0x * (q0y * (-s000 + s100) + q0w * (s020 - s120)) + q0y * q0z * s120 -
                    qperpw * qperpx * s120 + qperpy * qperpz * s120,
                -(qperpx * qperpy * s001) - qperpw * qperpz * s001 - s011 + q0z * q0z * s011 +
                    qperpx * qperpx * s011 + qperpz * qperpz * s011 - q0y * q0z * s021 +
                    qperpw * qperpx * s021 - qperpy * qperpz * s021 + qperpx * qperpy * s101 +
                    qperpw * qperpz * s101 + q0w * q0z * (-s001 + s101) +
                    q0x * q0x * (s011 - s111) + s111 - q0z * q0z * s111 -
                    qperpx * qperpx * s111 - qperpz * qperpz * s111 +
                    q0x * (q0y * (-s001 + s101) + q0w * (s021 - s121)) + q0y * q0z * s121 -
                    qperpw * qperpx * s121 + qperpy * qperpz * s121,
                -(qperpx * qperpy * s002) - qperpw * qperpz * s002 - s012 + q0z * q0z * s012 +
                    qperpx * qperpx * s012 + qperpz * qperpz * s012 - q0y * q0z * s022 +
                    qperpw * qperpx * s022 - qperpy * qperpz * s022 + qperpx * qperpy * s102 +
                    qperpw * qperpz * s102 + q0w * q0z * (-s002 + s102) +
                    q0x * q0x * (s012 - s112) + s112 - q0z * q0z * s112 -
                    qperpx * qperpx * s112 - qperpz * qperpz * s112 +
                    q0x * (q0y * (-s002 + s102) + q0w * (s022 - s122)) + q0y * q0z * s122 -
                    qperpw * qperpx * s122 + qperpy * qperpz * s122);

            c2[1] = new DerivativeTerm(
                0,
                qperpx * qperpy * s000 + qperpw * qperpz * s000 + q0z * q0z * s010 -
                    qperpx * qperpx * s010 - qperpz * qperpz * s010 - q0y * q0z * s020 -
                    qperpw * qperpx * s020 + qperpy * qperpz * s020 - qperpx * qperpy * s100 -
                    qperpw * qperpz * s100 + q0x * q0x * (s010 - s110) - q0z * q0z * s110 +
                    qperpx * qperpx * s110 + qperpz * qperpz * s110 + q0y * q0z * s120 +
                    qperpw * qperpx * s120 - qperpy * qperpz * s120 +
                    2 * q0z * qperpw * s000 * theta + 2 * q0y * qperpx * s000 * theta -
                    4 * q0z * qperpz * s010 * theta + 2 * q0z * qperpy * s020 * theta +
                    2 * q0y * qperpz * s020 * theta +
                    q0x * (q0w * s020 + q0y * (-s000 + s100) - q0w * s120 +
                           2 * qperpy * s000 * theta - 4 * qperpx * s010 * theta -
                           2 * qperpw * s020 * theta) +
                    q0w * (-(q0z * s000) + q0z * s100 + 2 * qperpz * s000 * theta -
                           2 * qperpx * s020 * theta),
                qperpx * qperpy * s001 + qperpw * qperpz * s001 + q0z * q0z * s011 -
                    qperpx * qperpx * s011 - qperpz * qperpz * s011 - q0y * q0z * s021 -
                    qperpw * qperpx * s021 + qperpy * qperpz * s021 - qperpx * qperpy * s101 -
                    qperpw * qperpz * s101 + q0x * q0x * (s011 - s111) - q0z * q0z * s111 +
                    qperpx * qperpx * s111 + qperpz * qperpz * s111 + q0y * q0z * s121 +
                    qperpw * qperpx * s121 - qperpy * qperpz * s121 +
                    2 * q0z * qperpw * s001 * theta + 2 * q0y * qperpx * s001 * theta -
                    4 * q0z * qperpz * s011 * theta + 2 * q0z * qperpy * s021 * theta +
                    2 * q0y * qperpz * s021 * theta +
                    q0x * (q0w * s021 + q0y * (-s001 + s101) - q0w * s121 +
                           2 * qperpy * s001 * theta - 4 * qperpx * s011 * theta -
                           2 * qperpw * s021 * theta) +
                    q0w * (-(q0z * s001) + q0z * s101 + 2 * qperpz * s001 * theta -
                           2 * qperpx * s021 * theta),
                qperpx * qperpy * s002 + qperpw * qperpz * s002 + q0z * q0z * s012 -
                    qperpx * qperpx * s012 - qperpz * qperpz * s012 - q0y * q0z * s022 -
                    qperpw * qperpx * s022 + qperpy * qperpz * s022 - qperpx * qperpy * s102 -
                    qperpw * qperpz * s102 + q0x * q0x * (s012 - s112) - q0z * q0z * s112 +
                    qperpx * qperpx * s112 + qperpz * qperpz * s112 + q0y * q0z * s122 +
                    qperpw * qperpx * s122 - qperpy * qperpz * s122 +
                    2 * q0z * qperpw * s002 * theta + 2 * q0y * qperpx * s002 * theta -
                    4 * q0z * qperpz * s012 * theta + 2 * q0z * qperpy * s022 * theta +
                    2 * q0y * qperpz * s022 * theta +
                    q0x * (q0w * s022 + q0y * (-s002 + s102) - q0w * s122 +
                           2 * qperpy * s002 * theta - 4 * qperpx * s012 * theta -
                           2 * qperpw * s022 * theta) +
                    q0w * (-(q0z * s002) + q0z * s102 + 2 * qperpz * s002 * theta -
                           2 * qperpx * s022 * theta));

            c3[1] = new DerivativeTerm(
                0,
                2 *
                    (-(q0x * qperpy * s000) - q0w * qperpz * s000 + 2 * q0x * qperpx * s010 +
                     q0x * qperpw * s020 + q0w * qperpx * s020 + q0x * qperpy * s100 +
                     q0w * qperpz * s100 - 2 * q0x * qperpx * s110 - q0x * qperpw * s120 -
                     q0w * qperpx * s120 +
                     q0z * (2 * qperpz * s010 - qperpy * s020 + qperpw * (-s000 + s100) -
                            2 * qperpz * s110 + qperpy * s120) +
                     q0y *
                         (-(qperpx * s000) - qperpz * s020 + qperpx * s100 + qperpz * s120)) *
                    theta,
                2 *
                    (-(q0x * qperpy * s001) - q0w * qperpz * s001 + 2 * q0x * qperpx * s011 +
                     q0x * qperpw * s021 + q0w * qperpx * s021 + q0x * qperpy * s101 +
                     q0w * qperpz * s101 - 2 * q0x * qperpx * s111 - q0x * qperpw * s121 -
                     q0w * qperpx * s121 +
                     q0z * (2 * qperpz * s011 - qperpy * s021 + qperpw * (-s001 + s101) -
                            2 * qperpz * s111 + qperpy * s121) +
                     q0y *
                         (-(qperpx * s001) - qperpz * s021 + qperpx * s101 + qperpz * s121)) *
                    theta,
                2 *
                    (-(q0x * qperpy * s002) - q0w * qperpz * s002 + 2 * q0x * qperpx * s012 +
                     q0x * qperpw * s022 + q0w * qperpx * s022 + q0x * qperpy * s102 +
                     q0w * qperpz * s102 - 2 * q0x * qperpx * s112 - q0x * qperpw * s122 -
                     q0w * qperpx * s122 +
                     q0z * (2 * qperpz * s012 - qperpy * s022 + qperpw * (-s002 + s102) -
                            2 * qperpz * s112 + qperpy * s122) +
                     q0y *
                         (-(qperpx * s002) - qperpz * s022 + qperpx * s102 + qperpz * s122)) *
                    theta);

            c4[1] = new DerivativeTerm(
                0,
                -(q0x * qperpy * s000) - q0w * qperpz * s000 + 2 * q0x * qperpx * s010 +
                    q0x * qperpw * s020 + q0w * qperpx * s020 + q0x * qperpy * s100 +
                    q0w * qperpz * s100 - 2 * q0x * qperpx * s110 - q0x * qperpw * s120 -
                    q0w * qperpx * s120 + 2 * qperpx * qperpy * s000 * theta +
                    2 * qperpw * qperpz * s000 * theta + 2 * q0x * q0x * s010 * theta +
                    2 * q0z * q0z * s010 * theta - 2 * qperpx * qperpx * s010 * theta -
                    2 * qperpz * qperpz * s010 * theta + 2 * q0w * q0x * s020 * theta -
                    2 * qperpw * qperpx * s020 * theta + 2 * qperpy * qperpz * s020 * theta +
                    q0y * (-(qperpx * s000) - qperpz * s020 + qperpx * s100 + qperpz * s120 -
                           2 * q0x * s000 * theta) +
                    q0z * (2 * qperpz * s010 - qperpy * s020 + qperpw * (-s000 + s100) -
                           2 * qperpz * s110 + qperpy * s120 - 2 * q0w * s000 * theta -
                           2 * q0y * s020 * theta),
                -(q0x * qperpy * s001) - q0w * qperpz * s001 + 2 * q0x * qperpx * s011 +
                    q0x * qperpw * s021 + q0w * qperpx * s021 + q0x * qperpy * s101 +
                    q0w * qperpz * s101 - 2 * q0x * qperpx * s111 - q0x * qperpw * s121 -
                    q0w * qperpx * s121 + 2 * qperpx * qperpy * s001 * theta +
                    2 * qperpw * qperpz * s001 * theta + 2 * q0x * q0x * s011 * theta +
                    2 * q0z * q0z * s011 * theta - 2 * qperpx * qperpx * s011 * theta -
                    2 * qperpz * qperpz * s011 * theta + 2 * q0w * q0x * s021 * theta -
                    2 * qperpw * qperpx * s021 * theta + 2 * qperpy * qperpz * s021 * theta +
                    q0y * (-(qperpx * s001) - qperpz * s021 + qperpx * s101 + qperpz * s121 -
                           2 * q0x * s001 * theta) +
                    q0z * (2 * qperpz * s011 - qperpy * s021 + qperpw * (-s001 + s101) -
                           2 * qperpz * s111 + qperpy * s121 - 2 * q0w * s001 * theta -
                           2 * q0y * s021 * theta),
                -(q0x * qperpy * s002) - q0w * qperpz * s002 + 2 * q0x * qperpx * s012 +
                    q0x * qperpw * s022 + q0w * qperpx * s022 + q0x * qperpy * s102 +
                    q0w * qperpz * s102 - 2 * q0x * qperpx * s112 - q0x * qperpw * s122 -
                    q0w * qperpx * s122 + 2 * qperpx * qperpy * s002 * theta +
                    2 * qperpw * qperpz * s002 * theta + 2 * q0x * q0x * s012 * theta +
                    2 * q0z * q0z * s012 * theta - 2 * qperpx * qperpx * s012 * theta -
                    2 * qperpz * qperpz * s012 * theta + 2 * q0w * q0x * s022 * theta -
                    2 * qperpw * qperpx * s022 * theta + 2 * qperpy * qperpz * s022 * theta +
                    q0y * (-(qperpx * s002) - qperpz * s022 + qperpx * s102 + qperpz * s122 -
                           2 * q0x * s002 * theta) +
                    q0z * (2 * qperpz * s012 - qperpy * s022 + qperpw * (-s002 + s102) -
                           2 * qperpz * s112 + qperpy * s122 - 2 * q0w * s002 * theta -
                           2 * q0y * s022 * theta));

            c5[1] = new DerivativeTerm(
                0,
                -2 *
                    (qperpx * qperpy * s000 + qperpw * qperpz * s000 + q0z * q0z * s010 -
                     qperpx * qperpx * s010 - qperpz * qperpz * s010 - q0y * q0z * s020 -
                     qperpw * qperpx * s020 + qperpy * qperpz * s020 -
                     qperpx * qperpy * s100 - qperpw * qperpz * s100 +
                     q0w * q0z * (-s000 + s100) + q0x * q0x * (s010 - s110) -
                     q0z * q0z * s110 + qperpx * qperpx * s110 + qperpz * qperpz * s110 +
                     q0x * (q0y * (-s000 + s100) + q0w * (s020 - s120)) + q0y * q0z * s120 +
                     qperpw * qperpx * s120 - qperpy * qperpz * s120) *
                    theta,
                -2 *
                    (qperpx * qperpy * s001 + qperpw * qperpz * s001 + q0z * q0z * s011 -
                     qperpx * qperpx * s011 - qperpz * qperpz * s011 - q0y * q0z * s021 -
                     qperpw * qperpx * s021 + qperpy * qperpz * s021 -
                     qperpx * qperpy * s101 - qperpw * qperpz * s101 +
                     q0w * q0z * (-s001 + s101) + q0x * q0x * (s011 - s111) -
                     q0z * q0z * s111 + qperpx * qperpx * s111 + qperpz * qperpz * s111 +
                     q0x * (q0y * (-s001 + s101) + q0w * (s021 - s121)) + q0y * q0z * s121 +
                     qperpw * qperpx * s121 - qperpy * qperpz * s121) *
                    theta,
                -2 *
                    (qperpx * qperpy * s002 + qperpw * qperpz * s002 + q0z * q0z * s012 -
                     qperpx * qperpx * s012 - qperpz * qperpz * s012 - q0y * q0z * s022 -
                     qperpw * qperpx * s022 + qperpy * qperpz * s022 -
                     qperpx * qperpy * s102 - qperpw * qperpz * s102 +
                     q0w * q0z * (-s002 + s102) + q0x * q0x * (s012 - s112) -
                     q0z * q0z * s112 + qperpx * qperpx * s112 + qperpz * qperpz * s112 +
                     q0x * (q0y * (-s002 + s102) + q0w * (s022 - s122)) + q0y * q0z * s122 +
                     qperpw * qperpx * s122 - qperpy * qperpz * s122) *
                    theta);

            c1[2] = new DerivativeTerm(
                -t0z + t1z,
                (qperpw * qperpy * s000 - qperpx * qperpz * s000 - q0y * q0z * s010 -
                 qperpw * qperpx * s010 - qperpy * qperpz * s010 - s020 + q0y * q0y * s020 +
                 qperpx * qperpx * s020 + qperpy * qperpy * s020 - qperpw * qperpy * s100 +
                 qperpx * qperpz * s100 + q0x * q0z * (-s000 + s100) + q0y * q0z * s110 +
                 qperpw * qperpx * s110 + qperpy * qperpz * s110 +
                 q0w * (q0y * (s000 - s100) + q0x * (-s010 + s110)) +
                 q0x * q0x * (s020 - s120) + s120 - q0y * q0y * s120 -
                 qperpx * qperpx * s120 - qperpy * qperpy * s120),
                (qperpw * qperpy * s001 - qperpx * qperpz * s001 - q0y * q0z * s011 -
                 qperpw * qperpx * s011 - qperpy * qperpz * s011 - s021 + q0y * q0y * s021 +
                 qperpx * qperpx * s021 + qperpy * qperpy * s021 - qperpw * qperpy * s101 +
                 qperpx * qperpz * s101 + q0x * q0z * (-s001 + s101) + q0y * q0z * s111 +
                 qperpw * qperpx * s111 + qperpy * qperpz * s111 +
                 q0w * (q0y * (s001 - s101) + q0x * (-s011 + s111)) +
                 q0x * q0x * (s021 - s121) + s121 - q0y * q0y * s121 -
                 qperpx * qperpx * s121 - qperpy * qperpy * s121),
                (qperpw * qperpy * s002 - qperpx * qperpz * s002 - q0y * q0z * s012 -
                 qperpw * qperpx * s012 - qperpy * qperpz * s012 - s022 + q0y * q0y * s022 +
                 qperpx * qperpx * s022 + qperpy * qperpy * s022 - qperpw * qperpy * s102 +
                 qperpx * qperpz * s102 + q0x * q0z * (-s002 + s102) + q0y * q0z * s112 +
                 qperpw * qperpx * s112 + qperpy * qperpz * s112 +
                 q0w * (q0y * (s002 - s102) + q0x * (-s012 + s112)) +
                 q0x * q0x * (s022 - s122) + s122 - q0y * q0y * s122 -
                 qperpx * qperpx * s122 - qperpy * qperpy * s122));

            c2[2] = new DerivativeTerm(
                0,
                (q0w * q0y * s000 - q0x * q0z * s000 - qperpw * qperpy * s000 +
                 qperpx * qperpz * s000 - q0w * q0x * s010 - q0y * q0z * s010 +
                 qperpw * qperpx * s010 + qperpy * qperpz * s010 + q0x * q0x * s020 +
                 q0y * q0y * s020 - qperpx * qperpx * s020 - qperpy * qperpy * s020 -
                 q0w * q0y * s100 + q0x * q0z * s100 + qperpw * qperpy * s100 -
                 qperpx * qperpz * s100 + q0w * q0x * s110 + q0y * q0z * s110 -
                 qperpw * qperpx * s110 - qperpy * qperpz * s110 - q0x * q0x * s120 -
                 q0y * q0y * s120 + qperpx * qperpx * s120 + qperpy * qperpy * s120 -
                 2 * q0y * qperpw * s000 * theta + 2 * q0z * qperpx * s000 * theta -
                 2 * q0w * qperpy * s000 * theta + 2 * q0x * qperpz * s000 * theta +
                 2 * q0x * qperpw * s010 * theta + 2 * q0w * qperpx * s010 * theta +
                 2 * q0z * qperpy * s010 * theta + 2 * q0y * qperpz * s010 * theta -
                 4 * q0x * qperpx * s020 * theta - 4 * q0y * qperpy * s020 * theta),
                (q0w * q0y * s001 - q0x * q0z * s001 - qperpw * qperpy * s001 +
                 qperpx * qperpz * s001 - q0w * q0x * s011 - q0y * q0z * s011 +
                 qperpw * qperpx * s011 + qperpy * qperpz * s011 + q0x * q0x * s021 +
                 q0y * q0y * s021 - qperpx * qperpx * s021 - qperpy * qperpy * s021 -
                 q0w * q0y * s101 + q0x * q0z * s101 + qperpw * qperpy * s101 -
                 qperpx * qperpz * s101 + q0w * q0x * s111 + q0y * q0z * s111 -
                 qperpw * qperpx * s111 - qperpy * qperpz * s111 - q0x * q0x * s121 -
                 q0y * q0y * s121 + qperpx * qperpx * s121 + qperpy * qperpy * s121 -
                 2 * q0y * qperpw * s001 * theta + 2 * q0z * qperpx * s001 * theta -
                 2 * q0w * qperpy * s001 * theta + 2 * q0x * qperpz * s001 * theta +
                 2 * q0x * qperpw * s011 * theta + 2 * q0w * qperpx * s011 * theta +
                 2 * q0z * qperpy * s011 * theta + 2 * q0y * qperpz * s011 * theta -
                 4 * q0x * qperpx * s021 * theta - 4 * q0y * qperpy * s021 * theta),
                (q0w * q0y * s002 - q0x * q0z * s002 - qperpw * qperpy * s002 +
                 qperpx * qperpz * s002 - q0w * q0x * s012 - q0y * q0z * s012 +
                 qperpw * qperpx * s012 + qperpy * qperpz * s012 + q0x * q0x * s022 +
                 q0y * q0y * s022 - qperpx * qperpx * s022 - qperpy * qperpy * s022 -
                 q0w * q0y * s102 + q0x * q0z * s102 + qperpw * qperpy * s102 -
                 qperpx * qperpz * s102 + q0w * q0x * s112 + q0y * q0z * s112 -
                 qperpw * qperpx * s112 - qperpy * qperpz * s112 - q0x * q0x * s122 -
                 q0y * q0y * s122 + qperpx * qperpx * s122 + qperpy * qperpy * s122 -
                 2 * q0y * qperpw * s002 * theta + 2 * q0z * qperpx * s002 * theta -
                 2 * q0w * qperpy * s002 * theta + 2 * q0x * qperpz * s002 * theta +
                 2 * q0x * qperpw * s012 * theta + 2 * q0w * qperpx * s012 * theta +
                 2 * q0z * qperpy * s012 * theta + 2 * q0y * qperpz * s012 * theta -
                 4 * q0x * qperpx * s022 * theta - 4 * q0y * qperpy * s022 * theta));

            c3[2] = new DerivativeTerm(
                0,
                -2 *
                    (-(q0w * qperpy * s000) + q0x * qperpz * s000 + q0x * qperpw * s010 +
                     q0w * qperpx * s010 - 2 * q0x * qperpx * s020 + q0w * qperpy * s100 -
                     q0x * qperpz * s100 - q0x * qperpw * s110 - q0w * qperpx * s110 +
                     q0z * (qperpx * s000 + qperpy * s010 - qperpx * s100 - qperpy * s110) +
                     2 * q0x * qperpx * s120 +
                     q0y * (qperpz * s010 - 2 * qperpy * s020 + qperpw * (-s000 + s100) -
                            qperpz * s110 + 2 * qperpy * s120)) *
                    theta,
                -2 *
                    (-(q0w * qperpy * s001) + q0x * qperpz * s001 + q0x * qperpw * s011 +
                     q0w * qperpx * s011 - 2 * q0x * qperpx * s021 + q0w * qperpy * s101 -
                     q0x * qperpz * s101 - q0x * qperpw * s111 - q0w * qperpx * s111 +
                     q0z * (qperpx * s001 + qperpy * s011 - qperpx * s101 - qperpy * s111) +
                     2 * q0x * qperpx * s121 +
                     q0y * (qperpz * s011 - 2 * qperpy * s021 + qperpw * (-s001 + s101) -
                            qperpz * s111 + 2 * qperpy * s121)) *
                    theta,
                -2 *
                    (-(q0w * qperpy * s002) + q0x * qperpz * s002 + q0x * qperpw * s012 +
                     q0w * qperpx * s012 - 2 * q0x * qperpx * s022 + q0w * qperpy * s102 -
                     q0x * qperpz * s102 - q0x * qperpw * s112 - q0w * qperpx * s112 +
                     q0z * (qperpx * s002 + qperpy * s012 - qperpx * s102 - qperpy * s112) +
                     2 * q0x * qperpx * s122 +
                     q0y * (qperpz * s012 - 2 * qperpy * s022 + qperpw * (-s002 + s102) -
                            qperpz * s112 + 2 * qperpy * s122)) *
                    theta);

            c4[2] = new DerivativeTerm(
                0,
                q0w * qperpy * s000 - q0x * qperpz * s000 - q0x * qperpw * s010 -
                    q0w * qperpx * s010 + 2 * q0x * qperpx * s020 - q0w * qperpy * s100 +
                    q0x * qperpz * s100 + q0x * qperpw * s110 + q0w * qperpx * s110 -
                    2 * q0x * qperpx * s120 - 2 * qperpw * qperpy * s000 * theta +
                    2 * qperpx * qperpz * s000 * theta - 2 * q0w * q0x * s010 * theta +
                    2 * qperpw * qperpx * s010 * theta + 2 * qperpy * qperpz * s010 * theta +
                    2 * q0x * q0x * s020 * theta + 2 * q0y * q0y * s020 * theta -
                    2 * qperpx * qperpx * s020 * theta - 2 * qperpy * qperpy * s020 * theta +
                    q0z * (-(qperpx * s000) - qperpy * s010 + qperpx * s100 + qperpy * s110 -
                           2 * q0x * s000 * theta) +
                    q0y * (-(qperpz * s010) + 2 * qperpy * s020 + qperpw * (s000 - s100) +
                           qperpz * s110 - 2 * qperpy * s120 + 2 * q0w * s000 * theta -
                           2 * q0z * s010 * theta),
                q0w * qperpy * s001 - q0x * qperpz * s001 - q0x * qperpw * s011 -
                    q0w * qperpx * s011 + 2 * q0x * qperpx * s021 - q0w * qperpy * s101 +
                    q0x * qperpz * s101 + q0x * qperpw * s111 + q0w * qperpx * s111 -
                    2 * q0x * qperpx * s121 - 2 * qperpw * qperpy * s001 * theta +
                    2 * qperpx * qperpz * s001 * theta - 2 * q0w * q0x * s011 * theta +
                    2 * qperpw * qperpx * s011 * theta + 2 * qperpy * qperpz * s011 * theta +
                    2 * q0x * q0x * s021 * theta + 2 * q0y * q0y * s021 * theta -
                    2 * qperpx * qperpx * s021 * theta - 2 * qperpy * qperpy * s021 * theta +
                    q0z * (-(qperpx * s001) - qperpy * s011 + qperpx * s101 + qperpy * s111 -
                           2 * q0x * s001 * theta) +
                    q0y * (-(qperpz * s011) + 2 * qperpy * s021 + qperpw * (s001 - s101) +
                           qperpz * s111 - 2 * qperpy * s121 + 2 * q0w * s001 * theta -
                           2 * q0z * s011 * theta),
                q0w * qperpy * s002 - q0x * qperpz * s002 - q0x * qperpw * s012 -
                    q0w * qperpx * s012 + 2 * q0x * qperpx * s022 - q0w * qperpy * s102 +
                    q0x * qperpz * s102 + q0x * qperpw * s112 + q0w * qperpx * s112 -
                    2 * q0x * qperpx * s122 - 2 * qperpw * qperpy * s002 * theta +
                    2 * qperpx * qperpz * s002 * theta - 2 * q0w * q0x * s012 * theta +
                    2 * qperpw * qperpx * s012 * theta + 2 * qperpy * qperpz * s012 * theta +
                    2 * q0x * q0x * s022 * theta + 2 * q0y * q0y * s022 * theta -
                    2 * qperpx * qperpx * s022 * theta - 2 * qperpy * qperpy * s022 * theta +
                    q0z * (-(qperpx * s002) - qperpy * s012 + qperpx * s102 + qperpy * s112 -
                           2 * q0x * s002 * theta) +
                    q0y * (-(qperpz * s012) + 2 * qperpy * s022 + qperpw * (s002 - s102) +
                           qperpz * s112 - 2 * qperpy * s122 + 2 * q0w * s002 * theta -
                           2 * q0z * s012 * theta));

            c5[2] = new DerivativeTerm(
                0,
                2 *
                    (qperpw * qperpy * s000 - qperpx * qperpz * s000 + q0y * q0z * s010 -
                     qperpw * qperpx * s010 - qperpy * qperpz * s010 - q0y * q0y * s020 +
                     qperpx * qperpx * s020 + qperpy * qperpy * s020 +
                     q0x * q0z * (s000 - s100) - qperpw * qperpy * s100 +
                     qperpx * qperpz * s100 +
                     q0w * (q0y * (-s000 + s100) + q0x * (s010 - s110)) - q0y * q0z * s110 +
                     qperpw * qperpx * s110 + qperpy * qperpz * s110 + q0y * q0y * s120 -
                     qperpx * qperpx * s120 - qperpy * qperpy * s120 +
                     q0x * q0x * (-s020 + s120)) *
                    theta,
                2 *
                    (qperpw * qperpy * s001 - qperpx * qperpz * s001 + q0y * q0z * s011 -
                     qperpw * qperpx * s011 - qperpy * qperpz * s011 - q0y * q0y * s021 +
                     qperpx * qperpx * s021 + qperpy * qperpy * s021 +
                     q0x * q0z * (s001 - s101) - qperpw * qperpy * s101 +
                     qperpx * qperpz * s101 +
                     q0w * (q0y * (-s001 + s101) + q0x * (s011 - s111)) - q0y * q0z * s111 +
                     qperpw * qperpx * s111 + qperpy * qperpz * s111 + q0y * q0y * s121 -
                     qperpx * qperpx * s121 - qperpy * qperpy * s121 +
                     q0x * q0x * (-s021 + s121)) *
                    theta,
                2 *
                    (qperpw * qperpy * s002 - qperpx * qperpz * s002 + q0y * q0z * s012 -
                     qperpw * qperpx * s012 - qperpy * qperpz * s012 - q0y * q0y * s022 +
                     qperpx * qperpx * s022 + qperpy * qperpy * s022 +
                     q0x * q0z * (s002 - s102) - qperpw * qperpy * s102 +
                     qperpx * qperpz * s102 +
                     q0w * (q0y * (-s002 + s102) + q0x * (s012 - s112)) - q0y * q0z * s112 +
                     qperpw * qperpx * s112 + qperpy * qperpz * s112 + q0y * q0y * s122 -
                     qperpx * qperpx * s122 - qperpy * qperpy * s122 +
                     q0x * q0x * (-s022 + s122)) *
                    theta);
            }
        }

        // AnimatedTransform Public Methods
        public bool IsAnimated() { return actuallyAnimated; }

        public unsafe Ray Apply(Ray r, Float* tMax = null)
        {
            if (!actuallyAnimated || r.time <= startTime)
                return startTransform.Apply(r, tMax);
            else if (r.time >= endTime)
                return endTransform.Apply(r, tMax);
            else
            {
                Transform t = Interpolate(r.time);
                return t.Apply(r, tMax);
            }
        }

        public unsafe Ray ApplyInverse(Ray r, Float* tMax = null)
        {
            if (!actuallyAnimated || r.time <= startTime)
                return startTransform.ApplyInverse(r, tMax);
            else if (r.time >= endTime)
                return endTransform.ApplyInverse(r, tMax);
            else
            {
                Transform t = Interpolate(r.time);
                return t.ApplyInverse(r, tMax);
            }
        }

        public unsafe RayDifferential Apply(RayDifferential r, Float* tMax = null)
        {
            if (!actuallyAnimated || r.time <= startTime)
                return startTransform.Apply(r, tMax);
            else if (r.time >= endTime)
                return endTransform.Apply(r, tMax);
            else
            {
                Transform t = Interpolate(r.time);
                return t.Apply(r, tMax);
            }
        }

        public Point3f Apply(Point3f p, Float time)
        {
            if (!actuallyAnimated || time <= startTime)
                return startTransform.Apply(p);
            else if (time >= endTime)
                return endTransform.Apply(p);
            Transform t = Interpolate(time);
            return t.Apply(p);
        }

        public Point3f ApplyInverse(Point3f p, Float time)
        {
            if (!actuallyAnimated)
                return startTransform.ApplyInverse(p);
            return Interpolate(time).ApplyInverse(p);
        }

        public Vector3f Apply(Vector3f v, Float time)
        {
            if (!actuallyAnimated || time <= startTime)
                return startTransform.Apply(v);
            else if (time >= endTime)
                return endTransform.Apply(v);
            Transform t = Interpolate(time);
            return t.Apply(v);
        }

        public Vector3f ApplyInverse(Vector3f v, Float time)
        {
            if (!actuallyAnimated)
                return startTransform.ApplyInverse(v);
            return Interpolate(time).ApplyInverse(v);
        }

        public Normal3f Apply(Normal3f n, Float time)
        {
            if (!actuallyAnimated || time <= startTime)
                return startTransform.Apply(n);
            else if (time >= endTime)
                return endTransform.Apply(n);
            Transform t = Interpolate(time);
            return t.Apply(n);
        }

        public Normal3f ApplyInverse(Normal3f n, Float time)
        {
            if (!actuallyAnimated)
                return startTransform.ApplyInverse(n);
            return Interpolate(time).ApplyInverse(n);
        }

        public Interaction Apply(Interaction it)
        {
            if (!actuallyAnimated)
                return startTransform.Apply(it);
            Transform t = Interpolate(it.time);
            return t.Apply(it);
        }

        public Interaction ApplyInverse(Interaction it)
        {
            if (!actuallyAnimated)
                return startTransform.ApplyInverse(it);
            Transform t = Interpolate(it.time);
            return t.ApplyInverse(it);
        }

        public SurfaceInteraction Apply(SurfaceInteraction it)
        {
            if (!actuallyAnimated)
                return startTransform.Apply(it);
            Transform t = Interpolate(it.time);
            return t.Apply(it);
        }

        public SurfaceInteraction ApplyInverse(SurfaceInteraction it)
        {
            if (!actuallyAnimated)
                return startTransform.ApplyInverse(it);
            Transform t = Interpolate(it.time);
            return t.ApplyInverse(it);
        }

        public bool HasScale() { return startTransform.HasScale() || endTransform.HasScale(); }

        public override string ToString()
        {
            return $"[ LegacyAnimatedTransform startTransform: {startTransform} endTransform: {endTransform} " +
                   $"startTime: {startTime} endTime: {endTime} actuallyAnimated: {actuallyAnimated} T: [ {T[0]} {T[1]} ] " +
                   $"R: [ {R[0]} {R[1]} ] S: [ {S[0]} {S[1]} ] hasRotation: {hasRotation} ]";
        }

        public Transform Interpolate(Float time)
        {
            // Handle boundary conditions for matrix interpolation
            if (!actuallyAnimated || time <= startTime)
                return startTransform;
            if (time >= endTime)
                return endTransform;

            Float dt = (time - startTime) / (endTime - startTime);
            // Interpolate translation at _dt_
            Vector3f trans = (1 - dt) * T[0] + dt * T[1];

            // Interpolate rotation at _dt_
            Quaternion rotate = Slerp(dt, R[0], R[1]);

            // Interpolate scale at _dt_
            SquareMatrix4 scale = (1 - dt) * S[0] + dt * S[1];

            // Return computed final transform
            return Translate(trans) * new Transform(rotate) * new Transform(scale);
        }

        public Bounds3f MotionBounds(Bounds3f b)
        {
            if (!actuallyAnimated)
                return startTransform.Apply(b);
            if (!hasRotation)
                return Union(startTransform.Apply(b), endTransform.Apply(b));
            // Return motion bounds accounting for animated rotation
            Bounds3f bounds = new Bounds3f();
            for (int corner = 0; corner < 8; ++corner)
                bounds = Union(bounds, BoundPointMotion(b.Corner(corner)));
            return bounds;
        }

        public Bounds3f BoundPointMotion(Point3f p)
        {
            if (!actuallyAnimated)
                return new Bounds3f(startTransform.Apply(p));
            Bounds3f bounds = new Bounds3f(startTransform.Apply(p), endTransform.Apply(p));
            Float cosTheta = Dot(R[0], R[1]);
            Float theta = SafeACos(cosTheta);
            for (int c = 0; c < 3; ++c)
            {
                // Find any motion derivative zeros for the component _c_
                Float[] zeros = new Float[8];
                int nZeros = 0;
                unsafe
                {
                    FindZeros(c1[c].Eval(p), c2[c].Eval(p), c3[c].Eval(p), c4[c].Eval(p),
                              c5[c].Eval(p), theta, new Interval(0, 1), new pstd.span<Float>(zeros), &nZeros);
                }
                CHECK_LE(nZeros, zeros.Length);

                // Expand bounding box for any motion derivative zeros found
                for (int i = 0; i < nZeros; ++i)
                {
                    Point3f pz = Apply(p, Lerp(zeros[i], startTime, endTime));
                    bounds = Union(bounds, pz);
                }
            }
            return bounds;
        }

        // AnimatedTransform Public Members
        public Transform startTransform, endTransform;
        public Float startTime = 0, endTime = 1;

        // AnimatedTransform Private Methods
        private static unsafe void FindZeros(Float c1, Float c2, Float c3, Float c4, Float c5,
                                             Float theta, Interval tInterval,
                                             pstd.span<Float> zeros, int* nZeros, int depth = 8)
        {
            // Compute motion derivative interval
            Interval dadt =
                new Interval(c1) +
                (new Interval(c2) + new Interval(c3) * tInterval) * Cos(new Interval(2 * theta) * tInterval) +
                (new Interval(c4) + new Interval(c5) * tInterval) * Sin(new Interval(2 * theta) * tInterval);

            if (dadt.LowerBound() > 0 || dadt.UpperBound() < 0 ||
                dadt.LowerBound() == dadt.UpperBound())
                return;

            if (depth > 0 && dadt.Width() > 1e-3)
            {
                // Split _tInterval_ and check both resulting intervals
                Float mid = tInterval.Midpoint();
                FindZeros(c1, c2, c3, c4, c5, theta, new Interval(tInterval.LowerBound(), mid), zeros,
                          nZeros, depth - 1);
                FindZeros(c1, c2, c3, c4, c5, theta, new Interval(mid, tInterval.UpperBound()), zeros,
                          nZeros, depth - 1);

            }
            else
            {
                // Use Newton's method to refine zero
                Float tNewton = tInterval.Midpoint();
                for (int i = 0; i < 4; ++i)
                {
                    Float fNewton = c1 + (c2 + c3 * tNewton) * std.cos(2 * theta * tNewton) +
                                    (c4 + c5 * tNewton) * std.sin(2 * theta * tNewton);
                    Float fPrimeNewton =
                        (c3 + 2 * (c4 + c5 * tNewton) * theta) * std.cos(2 * tNewton * theta) +
                        (c5 - 2 * (c2 + c3 * tNewton) * theta) * std.sin(2 * tNewton * theta);
                    if (fNewton == 0 || fPrimeNewton == 0)
                        break;
                    tNewton = tNewton - fNewton / fPrimeNewton;
                }

                if (tNewton >= tInterval.LowerBound() - 1e-3f &&
                    tNewton < tInterval.UpperBound() + 1e-3f)
                {
                    zeros[*nZeros] = tNewton;
                    (*nZeros)++;
                }
            }
        }

        private bool actuallyAnimated = false;
        private Vector3f[] T = new Vector3f[2];
        private Quaternion[] R = new Quaternion[2];
        private SquareMatrix4[] S = new SquareMatrix4[2];
        private bool hasRotation;

        private struct DerivativeTerm
        {
            public DerivativeTerm(Float c, Float x, Float y, Float z) { kc = c; kx = x; ky = y; kz = z; }
            public Float kc, kx, ky, kz;
            public Float Eval(Point3f p) { return kc + kx * p.x + ky * p.y + kz * p.z; }
        }
        private DerivativeTerm[] c1 = new DerivativeTerm[3], c2 = new DerivativeTerm[3],
                                 c3 = new DerivativeTerm[3], c4 = new DerivativeTerm[3],
                                 c5 = new DerivativeTerm[3];
    }

}  // namespace pbrt
