#ifndef KARMAN_HD_COMMON
#define KARMAN_HD_COMMON
// Shared helpers for KÁRMÁN's HDRP surface shaders. Included after HDRP's LitData.hlsl, whose
// GetSurfaceAndBuiltinData is renamed away so each surface shader can provide its own.

float4x4 _KarmanSiteW2L;

struct KarmanSurface
{
    float3 albedo;
    float3 normalWS;
    float smoothness;
    float metallic;
    float occlusion;
    float3 emission;   // in HDRP luminance units (nits); multiplied by exposure by HDRP
};

float KHash31(float3 p) { p = frac(p * 0.1031); p += dot(p, p.zyx + 31.32); return frac((p.x + p.y) * p.z); }
float KNoise3(float3 p)
{
    float3 i = floor(p), f = frac(p); float3 u = f * f * (3 - 2 * f);
    return lerp(lerp(lerp(KHash31(i), KHash31(i + float3(1,0,0)), u.x), lerp(KHash31(i + float3(0,1,0)), KHash31(i + float3(1,1,0)), u.x), u.y),
                lerp(lerp(KHash31(i + float3(0,0,1)), KHash31(i + float3(1,0,1)), u.x), lerp(KHash31(i + float3(0,1,1)), KHash31(i + float3(1,1,1)), u.x), u.y), u.z);
}

void KarmanSurfaceToHDRP(FragInputs input, float3 V, inout PositionInputs posInput, KarmanSurface k, out SurfaceData surfaceData, out BuiltinData builtinData)
{
    ZERO_INITIALIZE(SurfaceData, surfaceData);
    surfaceData.materialFeatures = MATERIALFEATUREFLAGS_LIT_STANDARD;
    surfaceData.baseColor = saturate(k.albedo);
    surfaceData.normalWS = k.normalWS;
    surfaceData.geomNormalWS = input.tangentToWorld[2];
    surfaceData.perceptualSmoothness = saturate(k.smoothness);
    surfaceData.metallic = saturate(k.metallic);
    surfaceData.ambientOcclusion = saturate(k.occlusion);
    surfaceData.specularOcclusion = GetSpecularOcclusionFromAmbientOcclusion(ClampNdotV(dot(k.normalWS, V)), surfaceData.ambientOcclusion, PerceptualSmoothnessToRoughness(surfaceData.perceptualSmoothness));
    surfaceData.thickness = 1;
    surfaceData.ior = 1.5;
    surfaceData.transmittanceColor = 1;
    surfaceData.atDistance = 1;
    surfaceData.tangentWS = Orthonormalize(input.tangentToWorld[0], k.normalWS);

    LayerTexCoord layerTexCoord;
    ZERO_INITIALIZE(LayerTexCoord, layerTexCoord);
    GetBuiltinData(input, V, posInput, surfaceData, 1.0, k.normalWS, 0.0, layerTexCoord.base, builtinData);
    builtinData.emissiveColor = k.emission;
}
#endif
