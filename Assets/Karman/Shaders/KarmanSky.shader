Shader "Karman/Sky"
{
    // Equirectangular star/Milky Way background with physically dim exposure.
    Properties
    {
        [NoScaleOffset] _MainTex ("Stars (equirect)", 2D) = "black" {}
        _Exposure ("Exposure", Float) = 0.35
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "KarmanCommon.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float _Exposure;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                // Unity world (Y = celestial north) -> ICRS equatorial -> galactic (J2000 rotation matrix).
                float3 d = normalize(i.dir);
                float3 eq = float3(d.x, d.z, d.y);
                const float3x3 T = float3x3(-0.0548755604, -0.8734370902, -0.4838350155,
                                             0.4941094279, -0.4448296300,  0.7469822445,
                                            -0.8676661490, -0.1980763734,  0.4559837762);
                float3 g = mul(T, eq);
                float3 gObj = float3(g.x, g.z, -g.y); // panorama is viewed from inside: longitude increases leftward
                float3 col = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, KarmanEquirectUV(gObj), 0).rgb;
                return half4(col * _Exposure, 1);
            }
            ENDHLSL
        }
    }
}
