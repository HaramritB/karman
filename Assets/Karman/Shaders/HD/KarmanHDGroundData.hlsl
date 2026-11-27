// Launch-site ground for HDRP: anti-tiled scrub/lawn palette from CC0 scans, perimeter road, beach and ocean along the
// local coastline, fading into the global Earth imagery at the rim; city lights at night as emission. Lighting,
// shadows, reflections (ocean) and aerial perspective come from HDRP and the physically based sky.
#define GetSurfaceAndBuiltinData HDLit_GetSurfaceAndBuiltinData
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/LitData.hlsl"
#undef GetSurfaceAndBuiltinData
#include "KarmanHDCommon.hlsl"

TEXTURE2D(_DayTex); SAMPLER(sampler_DayTex);
TEXTURE2D(_NightTex); SAMPLER(sampler_NightTex);
TEXTURE2D(_SpecTex); SAMPLER(sampler_SpecTex);
TEXTURE2D(_GrassDiff); SAMPLER(sampler_GrassDiff);
TEXTURE2D(_GrassNor); SAMPLER(sampler_GrassNor);
TEXTURE2D(_GrassArm); SAMPLER(sampler_GrassArm);
TEXTURE2D(_MacroDiff); SAMPLER(sampler_MacroDiff);
TEXTURE2D(_SandDiff); SAMPLER(sampler_SandDiff);
TEXTURE2D(_SandNor); SAMPLER(sampler_SandNor);
float _NightIntensity, _KarmanUnlitScale;
float4 _SunDir;

float GHash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
float GNoise(float2 p) { float2 i = floor(p), f = frac(p); float2 u = f * f * (3 - 2 * f);
    return lerp(lerp(GHash(i), GHash(i + float2(1, 0)), u.x), lerp(GHash(i + float2(0, 1)), GHash(i + float2(1, 1)), u.x), u.y); }
float GFbm(float2 p) { float a = 0.5, s = 0; for (int k = 0; k < 5; k++) { s += a * GNoise(p); p *= 2.03; a *= 0.5; } return s; }
void NoTile(float2 uv, out float2 oa, out float2 ob, out float t)
{
    float l = GNoise(uv * 0.19) * 8.0;
    float ia = floor(l); t = smoothstep(0.25, 0.75, frac(l));
    oa = sin(float2(3.0, 7.0) * ia); ob = sin(float2(3.0, 7.0) * (ia + 1));
}
float4 Tex4(TEXTURE2D_PARAM(tx, ss), float2 uv, float2 oa, float2 ob, float t)
{
    float2 dx = ddx(uv), dy = ddy(uv);
    return lerp(SAMPLE_TEXTURE2D_GRAD(tx, ss, uv + oa, dx, dy), SAMPLE_TEXTURE2D_GRAD(tx, ss, uv + ob, dx, dy), t);
}
float Lum(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }
float RectRing(float2 p, float2 halfSize) { float2 d = abs(p) - halfSize; return abs(length(max(d, 0)) + min(max(d.x, d.y), 0)); }

void GetSurfaceAndBuiltinData(FragInputs input, float3 V, inout PositionInputs posInput, out SurfaceData surfaceData, out BuiltinData builtinData RAY_TRACING_OPTIONAL_PARAMETERS)
{
    float3 Ngeo = normalize(input.tangentToWorld[2]);
    float dist = length(input.positionRWS);
    float2 p = TransformWorldToObject(input.positionRWS).xz;
    float2 uvG = input.texCoord0.xy;
    float3 eastWS = normalize(TransformObjectToWorldDir(float3(1, 0, 0)));
    float3 northWS = normalize(TransformObjectToWorldDir(float3(0, 0, 1)));

    float wiggle = (GFbm(float2(p.y / 900.0, 3.1)) - 0.5) * 260.0;
    float shoreX = 1400.0 + p.y * 0.18 + wiggle;
    float localOcean = smoothstep(shoreX - 8.0, shoreX + 8.0, p.x);
    float beach = smoothstep(shoreX - 160.0, shoreX - 70.0, p.x) * (1 - localOcean);
    float localWeight = 1 - smoothstep(12000.0, 18000.0, length(p));
    float globalOcean = SAMPLE_TEXTURE2D(_SpecTex, sampler_SpecTex, uvG).r;
    float ocean = lerp(globalOcean, localOcean, localWeight);

    float2 o1a, o1b, o2a, o2b, o3a, o3b; float t1, t2, t3;
    float2 uv1 = p / 3.0, uv2 = p / 13.0, uv3 = p / 140.0;
    NoTile(uv1, o1a, o1b, t1); NoTile(uv2 + 7.3, o2a, o2b, t2); NoTile(uv3, o3a, o3b, t3);
    float3 g1 = Tex4(TEXTURE2D_ARGS(_GrassDiff, sampler_GrassDiff), uv1, o1a, o1b, t1).rgb;
    float3 g2 = Tex4(TEXTURE2D_ARGS(_GrassDiff, sampler_GrassDiff), uv2, o2a, o2b, t2).rgb;
    float3 gMean = SAMPLE_TEXTURE2D_LOD(_GrassDiff, sampler_GrassDiff, float2(0.5, 0.5), 12).rgb;
    float nearW = 1 - smoothstep(80.0, 900.0, dist);
    float3 detail = lerp(1.0.xxx, lerp(g2, lerp(g1, g2, 0.35), nearW) / max(gMean, 0.02), 0.85);
    float3 armG = Tex4(TEXTURE2D_ARGS(_GrassArm, sampler_GrassArm), uv1, o1a, o1b, t1).rgb;
    float3 nG = UnpackNormal(Tex4(TEXTURE2D_ARGS(_GrassNor, sampler_GrassNor), uv1, o1a, o1b, t1));
    float3 macro = Tex4(TEXTURE2D_ARGS(_MacroDiff, sampler_MacroDiff), uv3, o3a, o3b, t3).rgb;
    float3 mMean = SAMPLE_TEXTURE2D_LOD(_MacroDiff, sampler_MacroDiff, float2(0.5, 0.5), 12).rgb;
    float macroL = clamp(Lum(macro) / max(Lum(mMean), 0.02), 0.6, 1.45);
    float patchA = GFbm(p / 260.0), patchB = GFbm(p / 45.0 + 19.7), shrubs = GFbm(p / 9.0 + 3.3);
    float3 scrub = float3(0.075, 0.095, 0.045), dryGrass = float3(0.20, 0.18, 0.10), lawn = float3(0.065, 0.085, 0.04);
    float3 col = lerp(scrub, dryGrass, smoothstep(0.45, 0.72, patchA) * 0.7);
    col = lerp(col, col * 0.55, smoothstep(0.5, 0.7, shrubs) * (1 - smoothstep(0.55, 0.75, patchA)));
    col *= lerp(0.85, 1.15, patchB);
    float lawnMask = 1 - smoothstep(170.0, 230.0, max(abs(p.x) - 20.0, abs(p.y) + 10.0));
    lawnMask = max(lawnMask, (1 - smoothstep(22.0, 40.0, abs(p.x))) * step(p.y, -100.0) * (1 - smoothstep(2400.0, 2600.0, -p.y)));
    col = lerp(col, lawn, lawnMask);
    col *= detail * lerp(1.0, macroL, 0.7 * (1 - lawnMask * 0.6));
    float road = 1 - smoothstep(3.6, 4.4, RectRing(p - float2(0, -6), float2(122, 116)));
    float shoulder = (1 - smoothstep(5.0, 6.5, RectRing(p - float2(0, -6), float2(122, 116)))) * (1 - road);
    float asphaltN = GNoise(p * 1.7) * 0.5 + GNoise(p * 7.0) * 0.5;
    col = lerp(col, float3(0.07, 0.07, 0.068) * (0.85 + 0.3 * asphaltN), road);
    col = lerp(col, float3(0.24, 0.23, 0.21) * (0.8 + 0.4 * asphaltN), shoulder);

    float3 globalCol = SAMPLE_TEXTURE2D(_DayTex, sampler_DayTex, uvG).rgb;
    float detailFade = 1 - smoothstep(4000.0, 15000.0, length(p));
    float3 land = lerp(globalCol, col, detailFade * localWeight);
    float3 sand = SAMPLE_TEXTURE2D(_SandDiff, sampler_SandDiff, p / 3.0).rgb;
    float3 sandN = UnpackNormal(SAMPLE_TEXTURE2D(_SandNor, sampler_SandNor, p / 3.0));
    land = lerp(land, sand * float3(0.95, 0.9, 0.8), beach * localWeight);
    float flatten = road + shoulder;
    float3 tN = lerp(float3(0, 0, 1), lerp(nG, sandN, beach), nearW * (1 - ocean) * (1 - flatten));
    float3 N = normalize(eastWS * tN.x + northWS * tN.y + Ngeo * tN.z);
    float tm = _Time.y;
    float2 w1 = float2(GNoise(p / 6.0 + tm * 0.3), GNoise(p / 6.0 + 17.0 - tm * 0.25)) - 0.5;
    float2 w2 = float2(GNoise(p / 1.7 - tm * 0.6), GNoise(p / 1.7 + 5.0 + tm * 0.5)) - 0.5;
    float3 Nwater = normalize(Ngeo + (eastWS * (w1.x + w2.x * 0.5) + northWS * (w1.y + w2.y * 0.5)) * 0.18);

    float rough = lerp(lerp(armG.g, 0.9, flatten), 0.8, beach);
    KarmanSurface k;
    k.albedo = lerp(land, float3(0.015, 0.035, 0.05), ocean);
    k.normalWS = normalize(lerp(N, Nwater, ocean));
    k.smoothness = lerp(lerp(1 - rough, 0.08, 1 - nearW), 0.93, ocean);
    k.metallic = 0;
    k.occlusion = lerp(1, armG.r, nearW * (1 - ocean) * (1 - flatten));
    float night = 1 - smoothstep(-0.12, 0.06, dot(Ngeo, normalize(_SunDir.xyz)));
    k.emission = pow(SAMPLE_TEXTURE2D(_NightTex, sampler_NightTex, uvG).rgb, 1.6) * float3(1, 0.82, 0.55) * night * _NightIntensity * (1 - nearW)
                 * (_KarmanUnlitScale > 0 ? _KarmanUnlitScale : 38197.0);
    KarmanSurfaceToHDRP(input, V, posInput, k, surfaceData, builtinData);
}
