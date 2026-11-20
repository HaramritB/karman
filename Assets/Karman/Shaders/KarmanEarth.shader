Shader "Karman/Earth"
{
    Properties
    {
        [NoScaleOffset] _DayTex ("Day (Blue Marble)", 2D) = "white" {}
        [NoScaleOffset] _NightTex ("Night lights", 2D) = "black" {}
        [NoScaleOffset] _CloudTex ("Clouds", 2D) = "black" {}
        [NoScaleOffset] _NormalTex ("Normal", 2D) = "bump" {}
        [NoScaleOffset] _SpecTex ("Ocean mask", 2D) = "black" {}
        _SunIntensity ("Sun intensity", Float) = 4
        _NightIntensity ("City lights intensity", Float) = 2.5
        _NormalStrength ("Normal strength", Range(0,2)) = 0.6
        _CloudOffset ("Cloud longitude offset", Float) = 0
        _CloudShadow ("Cloud shadow strength", Range(0,1)) = 0.55
        _OceanColor ("Ocean tint", Color) = (0.02, 0.06, 0.12, 1)
        _ZenithOpticalDepth ("Zenith optical depth (RGB)", Vector) = (0.046, 0.108, 0.265, 0)
        _SunDir ("Sun direction (world)", Vector) = (1, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="HDRenderPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="ForwardOnly" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment KFrag
            #include "HD/KarmanHDUnlit.hlsl"
            #include "KarmanCommon.hlsl"

            TEXTURE2D(_DayTex); SAMPLER(sampler_DayTex);
            TEXTURE2D(_NightTex); SAMPLER(sampler_NightTex);
            TEXTURE2D(_CloudTex); SAMPLER(sampler_CloudTex);
            TEXTURE2D(_NormalTex); SAMPLER(sampler_NormalTex);
            TEXTURE2D(_SpecTex); SAMPLER(sampler_SpecTex);

            CBUFFER_START(UnityPerMaterial)
                float _SunIntensity, _NightIntensity, _NormalStrength, _CloudOffset, _CloudShadow;
                float4 _OceanColor, _ZenithOpticalDepth, _SunDir;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dirOS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.dirOS = normalize(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(o.dirOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 dOS = normalize(i.dirOS);
                float2 uv = KarmanEquirectUV(dOS);
                float3 N = normalize(i.normalWS);
                float3 L = normalize(_SunDir.xyz);
                float3 V = normalize(_WorldSpaceCameraPos - i.positionWS);

                // Local tangent frame: east/north on the sphere, in world space.
                float3 poleWS = normalize(TransformObjectToWorldDir(float3(0, 1, 0)));
                float3 east = normalize(cross(poleWS, N) + 1e-6);
                float3 north = cross(N, east);

                float3 albedo = SAMPLE_TEXTURE2D(_DayTex, sampler_DayTex, uv).rgb;
                float ocean = SAMPLE_TEXTURE2D(_SpecTex, sampler_SpecTex, uv).r;
                float3 nT = UnpackNormal(SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, uv));
                nT.xy *= _NormalStrength * (1.0 - ocean);
                float3 Nd = normalize(east * nT.x + north * nT.y + N * nT.z);

                float NdotL = dot(N, L);
                float3 sunColor = KarmanSunTransmittance(NdotL, _ZenithOpticalDepth.xyz) * _SunIntensity;

                // Clouds and their shadows (shadow sampled along the projected sun direction).
                float2 cuv = uv + float2(_CloudOffset, 0);
                float cloud = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, cuv).r;
                float lat = asin(clamp(dOS.y, -1, 1));
                float k = 0.0012 / max(NdotL, 0.08);
                float2 shadowUV = cuv + float2(dot(L, east) * k / (2 * K_PI * max(cos(lat), 0.05)), dot(L, north) * k / K_PI);
                float cloudShadow = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, shadowUV).r;
                float groundLight = 1.0 - _CloudShadow * cloudShadow;

                float wrap = saturate((dot(Nd, L) + 0.05) / 1.05);
                float3 ground = albedo * sunColor * wrap * groundLight;
                ground = lerp(ground, ground * 0.6 + _OceanColor.rgb * sunColor * wrap * 0.4, ocean);

                // Ocean sun glint (GGX + Schlick).
                float3 H = normalize(L + V);
                float NdotH = saturate(dot(N, H));
                float fresnel = 0.02 + 0.98 * pow(1.0 - saturate(dot(V, H)), 5.0);
                float glint = KarmanGGX(NdotH, 0.22) * fresnel * saturate(NdotL) * 0.25;
                ground += ocean * glint * sunColor * groundLight * (1.0 - cloud);

                // Clouds lit with a soft terminator.
                float cloudLit = saturate((NdotL + 0.08) / 1.08);
                float3 cloudCol = sunColor * cloudLit * 0.95;
                float3 col = lerp(ground, cloudCol, cloud);

                // City lights on the night side, dimmed by clouds.
                float night = 1.0 - smoothstep(-0.12, 0.06, NdotL);
                float3 lights = SAMPLE_TEXTURE2D(_NightTex, sampler_NightTex, uv).rgb;
                lights = pow(lights, 1.6) * float3(1.0, 0.82, 0.55);
                col += lights * night * (1.0 - cloud * 0.85) * _NightIntensity;

                return half4(col, 1);
            }
            float4 KFrag(Varyings i) : SV_Target { float4 c = frag(i); c.rgb *= KarmanExposure(); return c; }
            ENDHLSL
        }
            Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode"="DepthForwardOnly" }
            ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma vertex KDepthV
            #pragma fragment KDepthF
            #include "HD/KarmanHDUnlit.hlsl"
            float4 KDepthV(float4 positionOS : POSITION) : SV_POSITION { return TransformWorldToHClip(TransformObjectToWorld(positionOS.xyz)); }
            void KDepthF() {}
            ENDHLSL
        }
    }
}
