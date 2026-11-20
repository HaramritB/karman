Shader "Karman/Glare"
{
    // Camera-facing solar glare; drawn on a quad that the SunView keeps at a fixed angular size.
    Properties { _Color ("Colour", Color) = (1, 0.92, 0.8, 1) _Intensity ("Intensity", Float) = 2 }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+20" "RenderPipeline"="HDRenderPipeline" }
        Pass
        {
            Tags { "LightMode"="ForwardOnly" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment KFrag
            #include "HD/KarmanHDUnlit.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color; float _Intensity;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes v) { Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.uv = v.uv * 2 - 1; return o; }
            half4 frag(Varyings i) : SV_Target
            {
                float r = length(i.uv);
                float glow = exp(-r * 9.0) * 1.5 + exp(-r * 3.0) * 0.12;
                float ang = atan2(i.uv.y, i.uv.x);
                float rays = pow(abs(cos(ang * 3.0)), 60.0) * exp(-r * 4.0) * 0.5;
                return half4(_Color.rgb * (glow + rays) * _Intensity * saturate(1 - r), 0);
            }
            float4 KFrag(Varyings i) : SV_Target { float4 c = frag(i); c.rgb = min(c.rgb * KarmanExposure(), 30000.0); return c; }
            ENDHLSL
        }
    }
}
