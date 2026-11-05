// (not in pbrt-v4) Open Shading Language integration: the closure primitives.
//
// The closures that a shader can create are registered with the shading system in the way testrender (src/testrender/shading.cpp) does it:
// each has an id, and a list of ClosureParam that describe where the arguments live in the closure's data. The ids are those of testrender's
// ClosureIDs. The data of a closure is the argument block that follows the ClosureComponent in the closure pool; the readers below
// take the fields out of it with the same offsets that the registration uses.
//
// The MaterialX closures of OSL 1.16 (stdosl.h, MATERIALX_CLOSURES) are registered with the parameter blocks of libbsdl (the structures
// in BSDL/MTX/*_decl.h): dielectric_bsdf, generalized_schlick_bsdf and sheen_bsdf start with the pointer of LayeredData, which is
// not an argument.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using OIIO;
using OSL;

namespace pbrt
{
    /// The ids of the closure primitives (those of testrender's ClosureIDs)
    public static class OSLClosureId
    {
        public const int Emission = 1;
        public const int Background = 2;
        public const int Diffuse = 3;
        public const int OrenNayar = 4;
        public const int Translucent = 5;
        public const int Phong = 6;
        public const int Ward = 7;
        public const int Microfacet = 8;
        public const int Reflection = 9;
        public const int FresnelReflection = 10;
        public const int Refraction = 11;
        public const int Transparent = 12;
        public const int Debug = 13;
        public const int Holdout = 14;
        public const int MxOrenNayarDiffuse = 15;
        public const int MxBurleyDiffuse = 16;
        public const int MxDielectric = 17;
        public const int MxConductor = 18;
        public const int MxGeneralizedSchlick = 19;
        public const int MxTranslucent = 20;
        public const int MxTransparent = 21;
        public const int MxSubsurface = 22;
        public const int MxSheen = 23;
        public const int MxUniformEdf = 24;
        public const int MxAnisotropicVdf = 25;
        public const int MxMediumVdf = 26;
        public const int MxLayer = 27;
        public const int Subsurface = 28;
    }

    /// Reads the fields of the argument block of a closure
    public static unsafe class OSLClosureData
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ReadFloat(void* data, int offset)
        {
            return *(float*)((byte*)data + offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ReadInt(void* data, int offset)
        {
            return *(int*)((byte*)data + offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec3 ReadVec3(void* data, int offset)
        {
            return *(Vec3*)((byte*)data + offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Color3 ReadColor3(void* data, int offset)
        {
            float* p = (float*)((byte*)data + offset);
            return new Color3(p[0], p[1], p[2]);
        }

        public static string ReadString(void* data, int offset)
        {
            ulong hash = *(ulong*)((byte*)data + offset);
            if (hash == 0)
            {
                return "";
            }
            return OslStrings.from_hash(hash).@string();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ClosureColor* ReadClosure(void* data, int offset)
        {
            return *(ClosureColor**)((byte*)data + offset);
        }
    }

    public static unsafe class OSLClosures
    {
        // The offsets of the argument blocks (see the file comment)
        public const int OffsetN = 0;

        // microfacet: string dist; vector N, U; float xalpha, yalpha, eta; int refract
        public const int MicrofacetDist = 0;
        public const int MicrofacetN = 8;
        public const int MicrofacetU = 20;
        public const int MicrofacetXAlpha = 32;
        public const int MicrofacetYAlpha = 36;
        public const int MicrofacetEta = 40;
        public const int MicrofacetRefract = 44;
        public const int MicrofacetSize = 48;

        // dielectric_bsdf: LayeredData (8), N, U, refl_tint, refr_tint, roughness_x, roughness_y, IOR, distribution, thinfilm_thickness,
        // thinfilm_ior, dispersion, absorption
        public const int MxDielectricN = 8;
        public const int MxDielectricU = 20;
        public const int MxDielectricReflTint = 32;
        public const int MxDielectricRefrTint = 44;
        public const int MxDielectricRoughnessX = 56;
        public const int MxDielectricRoughnessY = 60;
        public const int MxDielectricIor = 64;
        public const int MxDielectricDistribution = 72;
        public const int MxDielectricThinFilmThickness = 80;
        public const int MxDielectricThinFilmIor = 84;
        public const int MxDielectricDispersion = 88;
        public const int MxDielectricAbsorption = 92;
        public const int MxDielectricSize = 104;

        // conductor_bsdf: N, U, roughness_x, roughness_y, IOR, extinction, distribution, thinfilm_thickness, thinfilm_ior
        public const int MxConductorN = 0;
        public const int MxConductorU = 12;
        public const int MxConductorRoughnessX = 24;
        public const int MxConductorRoughnessY = 28;
        public const int MxConductorIor = 32;
        public const int MxConductorExtinction = 44;
        public const int MxConductorDistribution = 56;
        public const int MxConductorThinFilmThickness = 64;
        public const int MxConductorThinFilmIor = 68;
        public const int MxConductorSize = 72;

        // generalized_schlick_bsdf: LayeredData (8), N, U, refl_tint, refr_tint, roughness_x, roughness_y, F0, F90, exponent, distribution
        public const int MxSchlickN = 8;
        public const int MxSchlickU = 20;
        public const int MxSchlickReflTint = 32;
        public const int MxSchlickRefrTint = 44;
        public const int MxSchlickRoughnessX = 56;
        public const int MxSchlickRoughnessY = 60;
        public const int MxSchlickF0 = 64;
        public const int MxSchlickF90 = 76;
        public const int MxSchlickExponent = 88;
        public const int MxSchlickDistribution = 96;
        public const int MxSchlickSize = 104;

        // sheen_bsdf: LayeredData (8), N, albedo, roughness, mode, label
        public const int MxSheenN = 8;
        public const int MxSheenAlbedo = 20;
        public const int MxSheenRoughness = 32;
        public const int MxSheenMode = 36;
        public const int MxSheenLabel = 40;
        public const int MxSheenSize = 48;

        // burley_diffuse_bsdf: N, albedo, roughness, label
        public const int MxBurleyN = 0;
        public const int MxBurleyAlbedo = 12;
        public const int MxBurleyRoughness = 24;
        public const int MxBurleyLabel = 32;
        public const int MxBurleySize = 40;

        // oren_nayar_diffuse_bsdf: N, albedo, roughness, energy_compensation, label
        public const int MxOrenNayarN = 0;
        public const int MxOrenNayarAlbedo = 12;
        public const int MxOrenNayarRoughness = 24;
        public const int MxOrenNayarEnergyCompensation = 28;
        public const int MxOrenNayarLabel = 32;
        public const int MxOrenNayarSize = 40;

        // translucent_bsdf: N, albedo, label
        public const int MxTranslucentN = 0;
        public const int MxTranslucentAlbedo = 12;
        public const int MxTranslucentLabel = 24;
        public const int MxTranslucentSize = 32;

        // subsurface_bssrdf: N, albedo, radius, anisotropy, label
        public const int MxSubsurfaceN = 0;
        public const int MxSubsurfaceAlbedo = 12;
        public const int MxSubsurfaceRadius = 24;
        public const int MxSubsurfaceAnisotropy = 36;
        public const int MxSubsurfaceLabel = 40;
        public const int MxSubsurfaceSize = 48;

        // subsurface (classic): float eta, g; color mfp, albedo
        public const int SubsurfaceEta = 0;
        public const int SubsurfaceG = 4;
        public const int SubsurfaceMfp = 8;
        public const int SubsurfaceAlbedo = 20;
        public const int SubsurfaceSize = 32;

        // uniform_edf: emittance, label
        public const int MxUniformEdfEmittance = 0;
        public const int MxUniformEdfLabel = 16;
        public const int MxUniformEdfSize = 24;

        // layer: closure top, base
        public const int MxLayerTop = 0;
        public const int MxLayerBase = 8;
        public const int MxLayerSize = 16;

        private static ClosureParam F(int offset)
        {
            return ClosureParam.CLOSURE_FLOAT_PARAM(offset);
        }

        private static ClosureParam I(int offset)
        {
            return ClosureParam.CLOSURE_INT_PARAM(offset);
        }

        private static ClosureParam C(int offset)
        {
            return ClosureParam.CLOSURE_COLOR_PARAM(offset);
        }

        private static ClosureParam V(int offset)
        {
            return ClosureParam.CLOSURE_VECTOR_PARAM(offset);
        }

        private static ClosureParam S(int offset)
        {
            return ClosureParam.CLOSURE_STRING_PARAM(offset);
        }

        private static ClosureParam Fk(int offset, string key)
        {
            return ClosureParam.CLOSURE_FLOAT_KEYPARAM(offset, key);
        }

        private static ClosureParam Ck(int offset, string key)
        {
            return ClosureParam.CLOSURE_COLOR_KEYPARAM(offset, key);
        }

        private static ClosureParam Ik(int offset, string key)
        {
            return ClosureParam.CLOSURE_INT_KEYPARAM(offset, key);
        }

        private static ClosureParam Sk(int offset, string key)
        {
            return ClosureParam.CLOSURE_STRING_KEYPARAM(offset, key);
        }

        private static ClosureParam Cl(int offset)
        {
            return ClosureParam.CLOSURE_CLOSURE_PARAM(offset);
        }

        private static ClosureParam Finish(int size, int alignment)
        {
            return ClosureParam.CLOSURE_FINISH_PARAM(size, alignment);
        }

        private static void Reg(ShadingSystem ss, string name, int id, params ClosureParam[] parameters)
        {
            ss.register_closure(name, id, parameters, null, null);
        }

        /// Registers every closure primitive that the integration knows with the shading system (testrender's register_closures)
        public static void Register(ShadingSystem ss)
        {
            Reg(ss, "emission", OSLClosureId.Emission, Finish(0, 1));
            Reg(ss, "background", OSLClosureId.Background, Finish(0, 1));
            Reg(ss, "diffuse", OSLClosureId.Diffuse, V(0), Finish(12, 4));
            Reg(ss, "oren_nayar", OSLClosureId.OrenNayar, V(0), F(12), Finish(16, 4));
            Reg(ss, "translucent", OSLClosureId.Translucent, V(0), Finish(12, 4));
            Reg(ss, "phong", OSLClosureId.Phong, V(0), F(12), Finish(16, 4));
            Reg(ss, "ward", OSLClosureId.Ward, V(0), V(12), F(24), F(28), Finish(32, 4));
            Reg(ss, "microfacet", OSLClosureId.Microfacet, S(MicrofacetDist), V(MicrofacetN), V(MicrofacetU), F(MicrofacetXAlpha),
                F(MicrofacetYAlpha), F(MicrofacetEta), I(MicrofacetRefract), Finish(MicrofacetSize, 8));
            // stdosl.h: reflection (N) is reflection (N, 0), and an eta of 0 means no Fresnel term
            Reg(ss, "reflection", OSLClosureId.FresnelReflection, V(0), F(12), Finish(16, 4));
            Reg(ss, "refraction", OSLClosureId.Refraction, V(0), F(12), Finish(16, 4));
            Reg(ss, "transparent", OSLClosureId.Transparent, Finish(0, 1));
            Reg(ss, "debug", OSLClosureId.Debug, S(0), Finish(8, 8));
            Reg(ss, "holdout", OSLClosureId.Holdout, Finish(0, 1));
            Reg(ss, "subsurface", OSLClosureId.Subsurface, F(SubsurfaceEta), F(SubsurfaceG), C(SubsurfaceMfp), C(SubsurfaceAlbedo),
                Finish(SubsurfaceSize, 4));

            // The MaterialX closures
            Reg(ss, "oren_nayar_diffuse_bsdf", OSLClosureId.MxOrenNayarDiffuse, V(MxOrenNayarN), C(MxOrenNayarAlbedo), F(MxOrenNayarRoughness),
                Ik(MxOrenNayarEnergyCompensation, "energy_compensation"), Sk(MxOrenNayarLabel, "label"), Finish(MxOrenNayarSize, 8));
            Reg(ss, "burley_diffuse_bsdf", OSLClosureId.MxBurleyDiffuse, V(MxBurleyN), C(MxBurleyAlbedo), F(MxBurleyRoughness),
                Sk(MxBurleyLabel, "label"), Finish(MxBurleySize, 8));
            Reg(ss, "dielectric_bsdf", OSLClosureId.MxDielectric, V(MxDielectricN), V(MxDielectricU), C(MxDielectricReflTint),
                C(MxDielectricRefrTint), F(MxDielectricRoughnessX), F(MxDielectricRoughnessY), F(MxDielectricIor), S(MxDielectricDistribution),
                Fk(MxDielectricThinFilmThickness, "thinfilm_thickness"), Fk(MxDielectricThinFilmIor, "thinfilm_ior"),
                Ck(MxDielectricAbsorption, "absorption"), Fk(MxDielectricDispersion, "dispersion"), Finish(MxDielectricSize, 8));
            Reg(ss, "conductor_bsdf", OSLClosureId.MxConductor, V(MxConductorN), V(MxConductorU), F(MxConductorRoughnessX),
                F(MxConductorRoughnessY), C(MxConductorIor), C(MxConductorExtinction), S(MxConductorDistribution),
                Fk(MxConductorThinFilmThickness, "thinfilm_thickness"), Fk(MxConductorThinFilmIor, "thinfilm_ior"), Finish(MxConductorSize, 8));
            Reg(ss, "generalized_schlick_bsdf", OSLClosureId.MxGeneralizedSchlick, V(MxSchlickN), V(MxSchlickU), C(MxSchlickReflTint),
                C(MxSchlickRefrTint), F(MxSchlickRoughnessX), F(MxSchlickRoughnessY), C(MxSchlickF0), C(MxSchlickF90), F(MxSchlickExponent),
                S(MxSchlickDistribution), Finish(MxSchlickSize, 8));
            Reg(ss, "translucent_bsdf", OSLClosureId.MxTranslucent, V(MxTranslucentN), C(MxTranslucentAlbedo), Sk(MxTranslucentLabel, "label"),
                Finish(MxTranslucentSize, 8));
            Reg(ss, "transparent_bsdf", OSLClosureId.MxTransparent, Finish(0, 1));
            Reg(ss, "subsurface_bssrdf", OSLClosureId.MxSubsurface, V(MxSubsurfaceN), C(MxSubsurfaceAlbedo), C(MxSubsurfaceRadius),
                F(MxSubsurfaceAnisotropy), Sk(MxSubsurfaceLabel, "label"), Finish(MxSubsurfaceSize, 8));
            Reg(ss, "sheen_bsdf", OSLClosureId.MxSheen, V(MxSheenN), C(MxSheenAlbedo), F(MxSheenRoughness), Ik(MxSheenMode, "mode"),
                Sk(MxSheenLabel, "label"), Finish(MxSheenSize, 8));
            Reg(ss, "uniform_edf", OSLClosureId.MxUniformEdf, C(MxUniformEdfEmittance), Sk(MxUniformEdfLabel, "label"),
                Finish(MxUniformEdfSize, 8));
            Reg(ss, "layer", OSLClosureId.MxLayer, Cl(MxLayerTop), Cl(MxLayerBase), Finish(MxLayerSize, 8));
            Reg(ss, "anisotropic_vdf", OSLClosureId.MxAnisotropicVdf, C(0), C(12), F(24), Sk(32, "label"), Finish(40, 8));
            Reg(ss, "medium_vdf", OSLClosureId.MxMediumVdf, C(0), F(12), C(16), F(28), F(32), I(36), Sk(40, "label"), Finish(48, 8));
        }
    }
}
