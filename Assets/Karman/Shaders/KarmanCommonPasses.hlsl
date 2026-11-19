#ifndef KARMAN_COMMON_PASSES
#define KARMAN_COMMON_PASSES
// Minimal ShadowCaster / DepthOnly / DepthNormals passes sharing the including shader's UnityPerMaterial CBUFFER
// (keeps materials SRP-batcher compatible and avoids UsePass layout mismatches).
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

float3 _LightDirection;
float3 _LightPosition;

struct KAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
struct KVaryings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

KVaryings KShadowVert(KAttributes v)
{
    KVaryings o;
    float3 p = TransformObjectToWorld(v.positionOS.xyz);
    float3 n = TransformObjectToWorldNormal(v.normalOS);
#if _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 l = normalize(_LightPosition - p);
#else
    float3 l = _LightDirection;
#endif
    float4 c = TransformWorldToHClip(ApplyShadowBias(p, n, l));
#if UNITY_REVERSED_Z
    c.z = min(c.z, UNITY_NEAR_CLIP_VALUE);
#else
    c.z = max(c.z, UNITY_NEAR_CLIP_VALUE);
#endif
    o.positionCS = c; o.normalWS = n;
    return o;
}

KVaryings KDepthVert(KAttributes v)
{
    KVaryings o;
    o.positionCS = TransformWorldToHClip(TransformObjectToWorld(v.positionOS.xyz));
    o.normalWS = TransformObjectToWorldNormal(v.normalOS);
    return o;
}

half4 KZeroFrag(KVaryings i) : SV_Target { return 0; }
half4 KNormalFrag(KVaryings i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
#endif
