Shader "Karman/Smoke"
{
    // Exhaust / pad cloud particles for HDRP: soft alpha-blended puffs lit by a simple sun + sky term, in the same
    // physical units (sun lux) as the rest of the scene.
    Properties { _MainTex ("Puff", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="HDRenderPipeline" }
        Pass
        {
            Tags { "LightMode"="ForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "HD/KarmanHDUnlit.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _KarmanSunDirWS;
            struct A { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            V vert(A v) { V o; o.positionCS = TransformWorldToHClip(TransformObjectToWorld(v.positionOS.xyz)); o.color = v.color; o.uv = v.uv; return o; }
            float4 frag(V i) : SV_Target
            {
                float4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color;
                float sunUp = saturate(_KarmanSunDirWS.w);
                float3 lit = t.rgb * (0.25 + 0.75 * sunUp) * 0.9;
                return float4(lit * KarmanExposure(), t.a);
            }
            ENDHLSL
        }
    }
}
