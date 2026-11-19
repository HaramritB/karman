Shader "Karman/MoonTerrain"
{
    // Quadtree terrain patches: geometric normals from the height field, LROC colour by direction (uv0 = unit
    // direction in object space), Lommel–Seeliger/Lambert regolith BRDF with opposition brightening, earthshine,
    // and a fine procedural regolith texture that fades in near the camera.
    Properties
    {
        [NoScaleOffset] _ColorTex ("LROC colour", 2D) = "gray" {}
        _SunIntensity ("Sun intensity", Float) = 4
        _Albedo ("Albedo", Float) = 0.75
        _EarthShine ("Earthshine", Float) = 0.02
        _SunDir ("Sun direction (world)", Vector) = (1,0,0,0)
        _EarthDir ("Earth direction (world)", Vector) = (0,0,1,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
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
            TEXTURE2D(_ColorTex); SAMPLER(sampler_ColorTex);
            CBUFFER_START(UnityPerMaterial)
                float _SunIntensity, _Albedo, _EarthShine;
                float4 _SunDir, _EarthDir;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float3 dir : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; float3 nWS : TEXCOORD1; float3 pWS : TEXCOORD2; float3 upWS : TEXCOORD3; };
            V vert(A v)
            {
                V o;
                o.pWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.pWS);
                o.dir = v.dir;
                o.nWS = TransformObjectToWorldNormal(v.normalOS);
                o.upWS = TransformObjectToWorldNormal(normalize(v.dir));
                return o;
            }
            float hash3(float3 p) { return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453); }
            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 albedo = SAMPLE_TEXTURE2D(_ColorTex, sampler_ColorTex, KarmanEquirectUV(d)).rgb * _Albedo;
                float3 N = normalize(i.nWS);
                float3 L = normalize(_SunDir.xyz);
                float3 V = normalize(_WorldSpaceCameraPos - i.pWS);
                float dist = length(_WorldSpaceCameraPos - i.pWS);
                // Fine regolith speckle near the camera (metre scale).
                float speck = lerp(1.0, 0.85 + 0.3 * hash3(floor(i.pWS * 2.0)), saturate(1 - dist / 400));
                float mu0 = saturate(dot(N, L));
                float mu = saturate(dot(N, V));
                float ls = 2.0 * mu0 / (mu0 + mu + 1e-4);
                float phase = dot(L, V);
                float opposition = 1 + 0.25 * pow(saturate(phase), 16);
                float brdf = lerp(mu0, ls, 0.6) * opposition * smoothstep(-0.02, 0.06, dot(normalize(i.upWS), L) + 0.05);
                float3 col = albedo * speck * _SunIntensity * brdf;
                col += albedo * _EarthShine * saturate(dot(N, normalize(_EarthDir.xyz))) * float3(0.55, 0.65, 0.9);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
