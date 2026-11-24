// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXGenShader/GenOptions.h.

using static MaterialX.ShaderInterfaceType;
using static MaterialX.HwSpecularEnvironmentMethod;
using static MaterialX.HwDirectionalAlbedoMethod;
using static MaterialX.HwTransmissionRenderMethod;

namespace MaterialX
{
    // Type of shader interface to be generated
    public enum ShaderInterfaceType
    {
        // Create a complete interface with uniforms for all
        // editable inputs on all nodes used by the shader.
        // This interface makes the shader fully editable by
        // value without requiring any rebuilds.
        // This is the default interface type.
        SHADER_INTERFACE_COMPLETE,

        // Create a reduced interface with uniforms only for
        // the inputs that has been declared in the shaders
        // nodedef interface. If no nodedef is used, the
        // interface will hold uniforms for all inputs on the
        // top-level shader node.
        SHADER_INTERFACE_REDUCED
    }

    // Method to use for specular environment lighting
    public enum HwSpecularEnvironmentMethod
    {
        // Do not use specular environment maps.
        SPECULAR_ENVIRONMENT_NONE,

        // Use Filtered Importance Sampling for
        // specular environment/indirect lighting.
        SPECULAR_ENVIRONMENT_FIS,

        // Use pre-filtered environment maps for
        // specular environment/indirect lighting.
        SPECULAR_ENVIRONMENT_PREFILTER
    }

    // Method to use for directional albedo evaluation
    public enum HwDirectionalAlbedoMethod
    {
        // Use an analytic approximation for directional albedo.
        DIRECTIONAL_ALBEDO_ANALYTIC,

        // Use a table look-up for directional albedo.
        DIRECTIONAL_ALBEDO_TABLE,

        // Use Monte Carlo integration for directional albedo.
        DIRECTIONAL_ALBEDO_MONTE_CARLO
    }

    // Method to use for transmission rendering
    public enum HwTransmissionRenderMethod
    {
        // Use a refraction approximation for transmission rendering
        TRANSMISSION_REFRACTION,

        // Use opacity for transmission rendering
        TRANSMISSION_OPACITY
    }

    // GenOptions Definition
    // Class holding options to configure shader generation.
    public class GenOptions
    {
        // GenOptions Public Methods
        public GenOptions()
        {
        }

        public GenOptions(GenOptions other)
        {
            assign(other);
        }

        // operator=
        public void assign(GenOptions other)
        {
            shaderInterfaceType = other.shaderInterfaceType;
            fileTextureVerticalFlip = other.fileTextureVerticalFlip;
            targetColorSpaceOverride = other.targetColorSpaceOverride;
            targetDistanceUnit = other.targetDistanceUnit;
            addUpstreamDependencies = other.addUpstreamDependencies;
            libraryPrefix = new FilePath(other.libraryPrefix);
            emitColorTransforms = other.emitColorTransforms;
            elideConstantNodes = other.elideConstantNodes;
            premultipliedBsdfAdd = other.premultipliedBsdfAdd;
            distributeLayerOverBsdfMix = other.distributeLayerOverBsdfMix;
            hwTransparency = other.hwTransparency;
            hwSpecularEnvironmentMethod = other.hwSpecularEnvironmentMethod;
            hwDirectionalAlbedoMethod = other.hwDirectionalAlbedoMethod;
            hwTransmissionRenderMethod = other.hwTransmissionRenderMethod;
            hwAiryFresnelIterations = other.hwAiryFresnelIterations;
            hwSrgbEncodeOutput = other.hwSrgbEncodeOutput;
            hwWriteDepthMoments = other.hwWriteDepthMoments;
            hwShadowMap = other.hwShadowMap;
            hwAmbientOcclusion = other.hwAmbientOcclusion;
            hwMaxActiveLightSources = other.hwMaxActiveLightSources;
            hwNormalizeUdimTexCoords = other.hwNormalizeUdimTexCoords;
            hwWriteAlbedoTable = other.hwWriteAlbedoTable;
            hwWriteEnvPrefilter = other.hwWriteEnvPrefilter;
            hwImplicitBitangents = other.hwImplicitBitangents;
            oslImplicitSurfaceShaderConversion = other.oslImplicitSurfaceShaderConversion;
            oslConnectCiWrapper = other.oslConnectCiWrapper;
        }

        // GenOptions Public Members
        // Sets the type of shader interface to be generated
        public ShaderInterfaceType shaderInterfaceType = SHADER_INTERFACE_COMPLETE;

        // If true the y-component of texture coordinates used for sampling file textures will be flipped before sampling.
        public bool fileTextureVerticalFlip = false;

        // An optional override for the target color space.
        public string targetColorSpaceOverride = "";

        // Define the target distance unit.
        public string targetDistanceUnit = "";

        // Sets whether to include upstream dependencies for the element to generate a shader for.
        public bool addUpstreamDependencies = true;

        // The standard library prefix, which will be applied to calls to emitLibraryInclude during code generation.
        public FilePath libraryPrefix = new FilePath("libraries");

        // Enable emitting colorspace transform code if a color management system is defined. Defaults to true.
        public bool emitColorTransforms = true;

        // Enable eliding constant nodes. Defaults to true.
        public bool elideConstantNodes = true;

        // Enable premultiplying the weights of mix nodes into their BSDF inputs when they are added together.
        public bool premultipliedBsdfAdd = false;

        // Enable distributing a layer node over a mix of BSDFs.
        public bool distributeLayerOverBsdfMix = false;

        // Sets if transparency is needed or not for HW shaders.
        public bool hwTransparency = false;

        // Sets the method to use for specular environment lighting for HW shader targets.
        public HwSpecularEnvironmentMethod hwSpecularEnvironmentMethod = SPECULAR_ENVIRONMENT_FIS;

        // Sets the method to use for directional albedo evaluation for HW shader targets.
        public HwDirectionalAlbedoMethod hwDirectionalAlbedoMethod = DIRECTIONAL_ALBEDO_ANALYTIC;

        // Sets the method to use for transmission rendering for HW shader targets.
        public HwTransmissionRenderMethod hwTransmissionRenderMethod = TRANSMISSION_REFRACTION;

        // Sets the number of iterations for the Airy Fresnel model.
        public uint hwAiryFresnelIterations = 2;

        // Enables an sRGB encoding for the color output on HW shader targets.
        public bool hwSrgbEncodeOutput = false;

        // Enables the writing of depth moments for HW shader targets.
        public bool hwWriteDepthMoments = false;

        // Enables shadow mapping for HW shader targets.
        public bool hwShadowMap = false;

        // Enables ambient occlusion rendering for HW shader targets.
        public bool hwAmbientOcclusion = false;

        // Sets the maximum number of light sources that can be active at once.
        public uint hwMaxActiveLightSources = 3;

        // Sets whether to transform texture coordinates to normalize uv space when UDIMs images are bound to an image.
        public bool hwNormalizeUdimTexCoords = false;

        // Enables the writing of a directional albedo table.
        public bool hwWriteAlbedoTable = false;

        // Enables the generation of a prefiltered environment map.
        public bool hwWriteEnvPrefilter = false;

        // Calculate fallback bitangents from existing normals and tangents inside the bitangent node.
        public bool hwImplicitBitangents = true;

        // Enables OSL conversion of surfaceshader struct to closure color.
        public bool oslImplicitSurfaceShaderConversion = true;

        // Add in a `setCi` node at the root of the shader graph (OSL networks).
        public bool oslConnectCiWrapper = false;
    }
}
