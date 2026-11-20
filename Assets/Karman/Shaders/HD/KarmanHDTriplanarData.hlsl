// KÁRMÁN triplanar PBR surface for HDRP (parts, pad structures, VAB hall).
// Projection space: 0 = absolute world, 1 = object, 2 = launch-site frame (_KarmanSiteW2L, absolute world → site).
#define GetSurfaceAndBuiltinData HDLit_GetSurfaceAndBuiltinData
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/LitData.hlsl"
#undef GetSurfaceAndBuiltinData
#include "KarmanHDCommon.hlsl"

TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
TEXTURE2D(_ArmMap); SAMPLER(sampler_ArmMap);
float _Tiling, _NormalStrength, _Roughness, _Desaturate, _AlbedoStrength, _ObjectSpace, _Grime, _GrimeScale, _Panel;
float4 _GrimeColor;

// Whiteout-blended triplanar normal mapping (Ben Golus); the top projection uses Inigo Quilez's noise-indexed
// double sampling so floors and decks never show a repeat grid.
void KTriplanar(float3 p, float3 n, out float3 albedo, out float3 arm, out float3 tnormal)
{
    float3 w = pow(abs(n), 4); w /= (w.x + w.y + w.z);
    float2 uvX = p.zy / _Tiling, uvY = p.xz / _Tiling, uvZ = p.xy / _Tiling;
    float l = KNoise3(float3(uvY * 0.21, 0.5)) * 8.0;
    float ia = floor(l), fl = frac(l);
    float2 oa = sin(float2(3.0, 7.0) * ia), ob = sin(float2(3.0, 7.0) * (ia + 1));
    float2 dx = ddx(uvY), dy = ddy(uvY);
    float3 ya = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, uvY + oa, dx, dy).rgb;
    float3 yb = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, uvY + ob, dx, dy).rgb;
    float tb = smoothstep(0.2, 0.8, fl - 0.1 * dot(ya - yb, 1));
    float3 albY = lerp(ya, yb, tb);
    float3 armY = lerp(SAMPLE_TEXTURE2D_GRAD(_ArmMap, sampler_ArmMap, uvY + oa, dx, dy).rgb, SAMPLE_TEXTURE2D_GRAD(_ArmMap, sampler_ArmMap, uvY + ob, dx, dy).rgb, tb);
    float3 nY = lerp(UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(_BumpMap, sampler_BumpMap, uvY + oa, dx, dy), _NormalStrength),
                     UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(_BumpMap, sampler_BumpMap, uvY + ob, dx, dy), _NormalStrength), tb);
    albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvX).rgb * w.x + albY * w.y + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvZ).rgb * w.z;
    arm = SAMPLE_TEXTURE2D(_ArmMap, sampler_ArmMap, uvX).rgb * w.x + armY * w.y + SAMPLE_TEXTURE2D(_ArmMap, sampler_ArmMap, uvZ).rgb * w.z;
    float3 nX = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvX), _NormalStrength);
    float3 nZ = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvZ), _NormalStrength);
    nX = float3(nX.xy + n.zy, abs(nX.z) * n.x);
    nY = float3(nY.xy + n.xz, abs(nY.z) * n.y);
    nZ = float3(nZ.xy + n.xy, abs(nZ.z) * n.z);
    tnormal = normalize(nX.zyx * w.x + nY.xzy * w.y + nZ.xyz * w.z);
}

void GetSurfaceAndBuiltinData(FragInputs input, float3 V, inout PositionInputs posInput, out SurfaceData surfaceData, out BuiltinData builtinData RAY_TRACING_OPTIONAL_PARAMETERS)
{
    float3 nWS = normalize(input.tangentToWorld[2]);
    float3 absWS = GetAbsolutePositionWS(input.positionRWS);
    float3 p, n;
    if (_ObjectSpace > 1.5) { p = mul(_KarmanSiteW2L, float4(absWS, 1)).xyz; n = normalize(mul((float3x3)_KarmanSiteW2L, nWS)); }
    else if (_ObjectSpace > 0.5) { p = TransformWorldToObject(input.positionRWS); n = normalize(TransformWorldToObjectNormal(nWS)); }
    else { p = absWS; n = nWS; }

    float3 albedoT, arm, nP;
    KTriplanar(p, n, albedoT, arm, nP);
    float3 N;
    if (_ObjectSpace > 1.5) N = normalize(mul(transpose((float3x3)_KarmanSiteW2L), nP));
    else if (_ObjectSpace > 0.5) N = normalize(TransformObjectToWorldNormal(nP));
    else N = nP;

    float lum = dot(albedoT, float3(0.299, 0.587, 0.114));
    albedoT = lerp(albedoT, lum.xxx, _Desaturate);
    float3 albedo = lerp(1.0.xxx, albedoT, _AlbedoStrength) * _BaseColor.rgb;
    float rough = arm.g * _Roughness;

    if (_Grime > 0.001)
    {
        float3 q = p / _GrimeScale;
        float stain = KNoise3(q) * 0.6 + KNoise3(q * 2.7 + 11.0) * 0.3 + KNoise3(q * 7.3 + 5.0) * 0.1;
        float streak = KNoise3(float3(q.x * 6.0, q.y * 0.35, q.z * 6.0)) * (1 - abs(n.y));
        float g = saturate((stain - 0.42) * 2.5 + streak * 0.6) * _Grime;
        albedo = lerp(albedo, albedo * _GrimeColor.rgb * 1.6, g);
        rough = saturate(rough + g * 0.25);
    }
    if (_Panel > 0.01 && n.y > 0.7)
    {
        float2 q = p.xz / _Panel;
        float2 cell = floor(q), f = frac(q);
        albedo *= 0.86 + 0.24 * KHash31(float3(cell, 3.7));
        float edge = min(min(f.x, 1 - f.x), min(f.y, 1 - f.y)) * _Panel;
        float w = fwidth(edge) + 1e-4;
        float joint = (1 - smoothstep(0.025, 0.025 + w, edge)) * saturate(0.08 / w);
        albedo *= 1 - 0.55 * joint;
        rough = lerp(rough, 1, joint);
    }

    KarmanSurface k;
    k.albedo = albedo; k.normalWS = N; k.smoothness = 1 - rough; k.metallic = _Metallic; k.occlusion = arm.r; k.emission = 0;
    KarmanSurfaceToHDRP(input, V, posInput, k, surfaceData, builtinData);
}
