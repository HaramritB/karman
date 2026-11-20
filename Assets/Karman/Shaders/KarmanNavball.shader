Shader "Karman/Navball"
{
    // Procedural attitude ball in the local horizon frame (object +Y = up, +Z = north, +X = east):
    // sky/ground hemispheres, 10° pitch lines, 30° heading meridians, north meridian highlighted.
    Properties { _Sky ("Sky", Color) = (0.16, 0.42, 0.72, 1) _Ground ("Ground", Color) = (0.42, 0.27, 0.14, 1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="HDRenderPipeline" }
        Pass
        {
            Tags { "LightMode"="ForwardOnly" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "HD/KarmanHDUnlit.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Sky, _Ground;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; float3 d : TEXCOORD0; float3 nWS : TEXCOORD1; };
            V vert(A v) { V o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.d = normalize(v.positionOS.xyz); o.nWS = TransformObjectToWorldNormal(v.normalOS); return o; }
            float lineMask(float x, float period, float width) { float f = abs(frac(x / period + 0.5) - 0.5) * period; return 1 - smoothstep(width * 0.5, width, f); }
            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.d);
                float elev = degrees(asin(clamp(d.y, -1, 1)));
                float hdg = degrees(atan2(d.x, d.z));          // 0 = north, 90 = east
                float3 col = d.y > 0 ? _Sky.rgb * (0.85 + 0.15 * d.y) : _Ground.rgb * (0.85 - 0.15 * d.y);
                float px = fwidth(elev) * 1.5;
                float pitchLines = lineMask(elev, 10, px) * (abs(elev) < 88 ? 1 : 0);
                float hdgLines = lineMask(hdg, 30, fwidth(hdg) * 1.5 / max(cos(radians(elev)), 0.2)) * (abs(elev) < 80 ? 1 : 0);
                col = lerp(col, float3(1, 1, 1), max(pitchLines, hdgLines) * 0.35);
                col = lerp(col, float3(1, 1, 1), lineMask(elev, 360, px * 2.5));           // horizon
                col = lerp(col, float3(1, 0.85, 0.2), lineMask(hdg, 360, fwidth(hdg) * 3) * (abs(elev) < 80 ? 1 : 0)); // north
                float shade = 0.55 + 0.45 * saturate(dot(normalize(i.nWS), float3(0, 0, -1)));
                return half4(col * shade, 1);
            }
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
