Shader "Karman/Ground"
{
    // Launch-site ground patch: global Earth imagery (same as the planet) plus procedural detail near the pad,
    // ocean specular from the land/sea mask, air-mass-reddened sunlight, city lights at night.
    Properties
    {
        [NoScaleOffset] _DayTex ("Day", 2D) = "white" {}
        [NoScaleOffset] _NightTex ("Night", 2D) = "black" {}
        [NoScaleOffset] _SpecTex ("Ocean mask", 2D) = "black" {}
        _SunIntensity ("Sun intensity", Float) = 4
        _NightIntensity ("City lights", Float) = 2.5
        _ZenithOpticalDepth ("Zenith optical depth", Vector) = (0.046, 0.108, 0.265, 0)
        _SunDir ("Sun direction (world)", Vector) = (0,1,0,0)
        _DetailScale ("Detail scale (m)", Float) = 35
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry-1" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "KarmanCommon.hlsl"
            TEXTURE2D(_DayTex); SAMPLER(sampler_DayTex);
            TEXTURE2D(_NightTex); SAMPLER(sampler_NightTex);
            TEXTURE2D(_SpecTex); SAMPLER(sampler_SpecTex);
            CBUFFER_START(UnityPerMaterial)
                float _SunIntensity, _NightIntensity, _DetailScale;
                float4 _ZenithOpticalDepth, _SunDir;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float rim : TEXCOORD1; float3 normalWS : TEXCOORD2; float3 positionWS : TEXCOORD3; float2 local : TEXCOORD4; };

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), u.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), u.x), u.y);
            }
            float fbm(float2 p) { float a = 0.5, s = 0; for (int k = 0; k < 5; k++) { s += a * noise(p); p *= 2.03; a *= 0.5; } return s; }

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = v.uv; o.rim = v.uv2.x;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.local = v.positionOS.xz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                float3 L = normalize(_SunDir.xyz);
                float3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                float3 albedo = SAMPLE_TEXTURE2D(_DayTex, sampler_DayTex, i.uv).rgb;
                float ocean = SAMPLE_TEXTURE2D(_SpecTex, sampler_SpecTex, i.uv).r;
                // The global mask is ~5 km/pixel; near the pad use a local coastline (Atlantic shore ~1.4 km east of
                // LC-39A, running NNW) with a sand beach, fading back to the global mask by ~18 km.
                float wiggle = (fbm(float2(i.local.y / 900.0, 3.1)) - 0.5) * 260.0;
                float shoreX = 1400.0 + i.local.y * 0.18 + wiggle;
                float localOcean = smoothstep(shoreX - 25.0, shoreX + 25.0, i.local.x);
                float beach = smoothstep(shoreX - 140.0, shoreX - 60.0, i.local.x) * (1 - localOcean);
                float localWeight = 1 - smoothstep(12000.0, 18000.0, length(i.local));
                ocean = lerp(ocean, localOcean, localWeight);
                // Procedural detail fades out towards the rim so the patch matches the globe there.
                float detailFade = 1 - smoothstep(0.25, 0.9, i.rim);
                float n1 = fbm(i.local / _DetailScale);
                float n2 = fbm(i.local / (_DetailScale * 14));
                float3 veg = lerp(float3(0.16, 0.20, 0.10), float3(0.30, 0.31, 0.20), n1);
                float3 detailed = lerp(albedo, albedo * 0.55 + veg * 0.6 * (0.8 + 0.4 * n2), 0.65);
                albedo = lerp(albedo, detailed, detailFade * (1 - ocean));
                albedo = lerp(albedo, float3(0.78, 0.72, 0.58), beach * localWeight);
                float NdotL = dot(N, L);
                float3 sun = KarmanSunTransmittance(NdotL, _ZenithOpticalDepth.xyz) * _SunIntensity;
                float3 col = albedo * sun * saturate(NdotL);
                // Water: darker, with a GGX sun glint.
                float3 H = normalize(L + V);
                float fres = 0.02 + 0.98 * pow(1 - saturate(dot(V, H)), 5);
                float3 water = float3(0.02, 0.05, 0.08) * sun * saturate(NdotL) + KarmanGGX(saturate(dot(N, H)), 0.12 + 0.1 * n1) * fres * saturate(NdotL) * sun * 0.25;
                col = lerp(col, water, ocean);
                float night = 1 - smoothstep(-0.12, 0.06, NdotL);
                col += pow(SAMPLE_TEXTURE2D(_NightTex, sampler_NightTex, i.uv).rgb, 1.6) * float3(1, 0.82, 0.55) * night * _NightIntensity;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
