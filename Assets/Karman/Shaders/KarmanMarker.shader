Shader "Karman/Marker"
{
    Properties { _Color ("Colour", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+50" "RenderPipeline"="HDRenderPipeline" }
        Pass
        {
            Tags { "LightMode"="ForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            ZTest Always
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment KFrag
            #include "HD/KarmanHDUnlit.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; };
            Varyings vert(Attributes v) { Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.color = v.color * _Color; return o; }
            half4 frag(Varyings i) : SV_Target { return i.color; }
            float4 KFrag(Varyings i) : SV_Target { float4 c = frag(i); c.rgb *= KarmanExposure(); return c; }
            ENDHLSL
        }
    }
}
