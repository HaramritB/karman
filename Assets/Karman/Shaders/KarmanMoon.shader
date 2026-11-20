Shader "Karman/Moon"
{
    Properties
    {
        [NoScaleOffset] _ColorTex ("LROC colour", 2D) = "gray" {}
        [NoScaleOffset] _SlopeTex ("LOLA slope normals (east,north,up)", 2D) = "bump" {}
        _SunIntensity ("Sun intensity", Float) = 4
        _Albedo ("Albedo boost", Float) = 0.75
        _NormalStrength ("Normal strength", Range(0, 4)) = 0.9
        _EarthShine ("Earthshine", Float) = 0.0
        _SunDir ("Sun direction (world)", Vector) = (1,0,0,0)
        _EarthDir ("Earth direction (world)", Vector) = (0,0,1,0)
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
            TEXTURE2D(_ColorTex); SAMPLER(sampler_ColorTex);
            TEXTURE2D(_SlopeTex); SAMPLER(sampler_SlopeTex);
            CBUFFER_START(UnityPerMaterial)
                float _SunIntensity, _Albedo, _NormalStrength, _EarthShine;
                float4 _SunDir, _EarthDir;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dirOS : TEXCOORD0; float3 positionWS : TEXCOORD1; float3 normalWS : TEXCOORD2; };
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
                float3 poleWS = normalize(TransformObjectToWorldDir(float3(0, 1, 0)));
                float3 east = normalize(cross(poleWS, N) + 1e-6);
                float3 north = cross(N, east);
                float3 s = SAMPLE_TEXTURE2D(_SlopeTex, sampler_SlopeTex, uv).xyz * 2.0 - 1.0;
                s.xy *= _NormalStrength;
                float3 Nd = normalize(east * s.x + north * s.y + N * s.z);
                float3 L = normalize(_SunDir.xyz);
                float3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                float3 albedo = SAMPLE_TEXTURE2D(_ColorTex, sampler_ColorTex, uv).rgb * _Albedo;
                // Fade relief at the terminator so slope normals cannot light pixels the sphere puts in shadow.
                float geo = dot(N, L);
                float mu0 = saturate(dot(Nd, L)) * smoothstep(-0.02, 0.08, geo);
                float mu = saturate(dot(Nd, V));
                // Lommel–Seeliger / Lambert blend: flat-looking full Moon, crisp terminator.
                float ls = 2.0 * mu0 / (mu0 + mu + 1e-4);
                float brdf = lerp(mu0, ls, 0.6);
                float3 col = albedo * _SunIntensity * brdf;
                col += albedo * _EarthShine * saturate(dot(Nd, normalize(_EarthDir.xyz))) * float3(0.55, 0.65, 0.9);
                return half4(col, 1);
            }
            float4 KFrag(Varyings i) : SV_Target { float4 c = frag(i); c.rgb = min(c.rgb * KarmanExposure(), 30000.0); return c; }
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
