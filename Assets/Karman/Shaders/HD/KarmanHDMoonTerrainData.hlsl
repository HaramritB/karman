// Lunar terrain patches for HDRP: LROC colour by direction (texCoord0 = unit direction in body space), triplanar CC0
// regolith detail on wrapped body-fixed coordinates (texCoord1), lit by HDRP (sun shadows, earthshine light).
#define GetSurfaceAndBuiltinData HDLit_GetSurfaceAndBuiltinData
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/LitData.hlsl"
#undef GetSurfaceAndBuiltinData
#include "KarmanHDCommon.hlsl"
#include "../KarmanCommon.hlsl"

TEXTURE2D(_ColorTex); SAMPLER(sampler_ColorTex);
TEXTURE2D(_NearDiff); SAMPLER(sampler_NearDiff);
TEXTURE2D(_NearNor); SAMPLER(sampler_NearNor);
TEXTURE2D(_MidDiff); SAMPLER(sampler_MidDiff);
TEXTURE2D(_MidNor); SAMPLER(sampler_MidNor);
float _Albedo;

void KTri(TEXTURE2D_PARAM(albedoTex, albedoSamp), TEXTURE2D_PARAM(norTex, norSamp), float3 p, float3 n, float tile, out float lum, out float3 nOut)
{
    float3 w = pow(abs(n), 4); w /= (w.x + w.y + w.z);
    float2 ux = p.zy / tile, uy = p.xz / tile, uz = p.xy / tile;
    float3 a = SAMPLE_TEXTURE2D(albedoTex, albedoSamp, ux).rgb * w.x + SAMPLE_TEXTURE2D(albedoTex, albedoSamp, uy).rgb * w.y + SAMPLE_TEXTURE2D(albedoTex, albedoSamp, uz).rgb * w.z;
    lum = dot(a, float3(0.299, 0.587, 0.114));
    float3 nx = UnpackNormal(SAMPLE_TEXTURE2D(norTex, norSamp, ux)); float3 ny = UnpackNormal(SAMPLE_TEXTURE2D(norTex, norSamp, uy)); float3 nz = UnpackNormal(SAMPLE_TEXTURE2D(norTex, norSamp, uz));
    nx = float3(nx.xy + n.zy, abs(nx.z) * n.x); ny = float3(ny.xy + n.xz, abs(ny.z) * n.y); nz = float3(nz.xy + n.xy, abs(nz.z) * n.z);
    nOut = normalize(nx.zyx * w.x + ny.xzy * w.y + nz.xyz * w.z);
}

void GetSurfaceAndBuiltinData(FragInputs input, float3 V, inout PositionInputs posInput, out SurfaceData surfaceData, out BuiltinData builtinData RAY_TRACING_OPTIONAL_PARAMETERS)
{
    float3 d = normalize(input.texCoord0.xyz);
    float3 detailP = input.texCoord1.xyz;
    float3 albedo = SAMPLE_TEXTURE2D(_ColorTex, sampler_ColorTex, KarmanEquirectUV(d)).rgb * _Albedo;
    float dist = length(input.positionRWS);
    float3 nOS = normalize(TransformWorldToObjectNormal(input.tangentToWorld[2]));
    float nearW = 1 - smoothstep(40.0, 260.0, dist);
    float midW = 1 - smoothstep(800.0, 6000.0, dist);
    float lumN = 0.5, lumM = 0.5; float3 nN = nOS, nM = nOS;
    if (midW > 0.001) KTri(TEXTURE2D_ARGS(_MidDiff, sampler_MidDiff), TEXTURE2D_ARGS(_MidNor, sampler_MidNor), detailP, nOS, 64.0, lumM, nM);
    if (nearW > 0.001) KTri(TEXTURE2D_ARGS(_NearDiff, sampler_NearDiff), TEXTURE2D_ARGS(_NearNor, sampler_NearNor), detailP, nOS, 2.0, lumN, nN);
    float detail = lerp(1, lumM / 0.42, midW * 0.55) * lerp(1, lumN / 0.45, nearW * 0.6);
    albedo *= clamp(detail, 0.4, 1.8);
    float3 nDet = normalize(lerp(nOS, nM, midW * 0.8));
    nDet = normalize(lerp(nDet, nN, nearW));

    KarmanSurface k;
    k.albedo = albedo; k.normalWS = normalize(TransformObjectToWorldNormal(nDet));
    k.smoothness = 0.08; k.metallic = 0; k.occlusion = 1; k.emission = 0;
    KarmanSurfaceToHDRP(input, V, posInput, k, surfaceData, builtinData);
}
