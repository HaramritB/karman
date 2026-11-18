Shader "Karman/Planet"
{
    // Distant-planet shader: texture, Lambert/Minnaert limb darkening and an optional thin atmospheric rim.
    Properties
    {
        [NoScaleOffset] _MainTex ("Surface", 2D) = "white" {}
        _SunIntensity ("Sun intensity", Float) = 4
        _Minnaert ("Minnaert k (limb darkening)", Range(0.5, 1.5)) = 0.9
        _RimColor ("Atmosphere rim colour", Color) = (0,0,0,0)
        _SunDir ("Sun direction (world)", Vector) = (1,0,0,0)
        _Emission ("Emission (Sun)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "KarmanCommon.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float _SunIntensity, _Minnaert, _Emission;
                float4 _RimColor, _SunDir;
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
                float3 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, KarmanEquirectUV(dOS)).rgb;
                float3 N = normalize(i.normalWS);
                float3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                float NdotV = saturate(dot(N, V));
                if (_Emission > 0)
                {
                    // Solar limb darkening (Eddington approximation).
                    float limb = 0.4 + 0.6 * NdotV;
                    return half4(albedo * _Emission * limb, 1);
                }
                float3 L = normalize(_SunDir.xyz);
                float NdotL = saturate(dot(N, L));
                float minnaert = pow(NdotL, _Minnaert) * pow(max(NdotV, 1e-3), _Minnaert - 1.0);
                float3 col = albedo * _SunIntensity * minnaert;
                float rim = pow(1.0 - NdotV, 4.0) * saturate(dot(N, L) + 0.3);
                col += _RimColor.rgb * _RimColor.a * rim * _SunIntensity;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
