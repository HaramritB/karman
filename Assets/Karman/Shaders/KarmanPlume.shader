Shader "Karman/Plume"
{
    // Additive exhaust plume. uv.y runs from nozzle (0) to tail (1). In vacuum the plume is wide and faint.
    Properties
    {
        _Color ("Core colour", Color) = (1.0, 0.75, 0.45, 1)
        _EdgeColor ("Edge colour", Color) = (0.35, 0.5, 1.0, 1)
        _Intensity ("Intensity", Float) = 3
        _Throttle ("Throttle", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="HDRenderPipeline" }
        Pass
        {
            Tags { "LightMode"="ForwardOnly" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment KFrag
            #include "HD/KarmanHDUnlit.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color, _EdgeColor;
                float _Intensity, _Throttle;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 positionWS : TEXCOORD2; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = v.uv;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                float facing = abs(dot(normalize(i.normalWS), V));
                float along = 1.0 - i.uv.y;
                float flicker = 0.9 + 0.1 * sin(_Time.y * 63.0 + i.uv.y * 20.0);
                float core = pow(facing, 2.0) * pow(along, 2.5);
                float3 col = lerp(_EdgeColor.rgb, _Color.rgb, pow(facing, 3.0)) * core * _Intensity * _Throttle * flicker;
                return half4(col, 0);
            }
            float4 KFrag(Varyings i) : SV_Target { float4 c = frag(i); c.rgb *= KarmanExposure(); return c; }
            ENDHLSL
        }
    }
}
