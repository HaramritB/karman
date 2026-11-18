Shader "Karman/Ring"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Ring colour+alpha (u = radius)", 2D) = "white" {}
        _SunIntensity ("Sun intensity", Float) = 4
        _SunDir ("Sun direction (world)", Vector) = (1,0,0,0)
        _PlanetCenter ("Planet centre (world)", Vector) = (0,0,0,0)
        _PlanetRadius ("Planet radius (world)", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "KarmanCommon.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float _SunIntensity, _PlanetRadius;
                float4 _SunDir, _PlanetCenter;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = v.uv;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(i.uv.x, 0.5));
                float3 L = normalize(_SunDir.xyz);
                float2 t = KarmanRaySphere(i.positionWS, L, _PlanetCenter.xyz, _PlanetRadius);
                float shadow = (t.x > 0 && t.x <= t.y) ? 0.08 : 1.0;
                return half4(c.rgb * _SunIntensity * 0.6 * shadow, c.a);
            }
            ENDHLSL
        }
    }
}
