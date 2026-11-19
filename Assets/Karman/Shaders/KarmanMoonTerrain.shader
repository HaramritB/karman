Shader "Karman/MoonTerrain"
{
    // Lunar terrain patches: geometric normals from the height field, LROC colour by direction, triplanar regolith
    // detail (2 m and 64 m tiling, CC0 scans, desaturated) on precise wrapped body-fixed coordinates, Lommel–Seeliger
    // regolith BRDF with opposition surge, earthshine, and sun shadows (e.g. the lander's) from URP.
    Properties
    {
        [NoScaleOffset] _ColorTex ("LROC colour", 2D) = "gray" {}
        [NoScaleOffset] _NearDiff ("Regolith near albedo", 2D) = "gray" {}
        [NoScaleOffset][Normal] _NearNor ("Regolith near normal", 2D) = "bump" {}
        [NoScaleOffset] _MidDiff ("Regolith mid albedo", 2D) = "gray" {}
        [NoScaleOffset][Normal] _MidNor ("Regolith mid normal", 2D) = "bump" {}
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "KarmanCommon.hlsl"
            TEXTURE2D(_ColorTex); SAMPLER(sampler_ColorTex);
            TEXTURE2D(_NearDiff); SAMPLER(sampler_NearDiff);
            TEXTURE2D(_NearNor); SAMPLER(sampler_NearNor);
            TEXTURE2D(_MidDiff); SAMPLER(sampler_MidDiff);
            TEXTURE2D(_MidNor); SAMPLER(sampler_MidNor);
            CBUFFER_START(UnityPerMaterial)
                float _SunIntensity, _Albedo, _EarthShine;
                float4 _SunDir, _EarthDir;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float3 dir : TEXCOORD0; float3 detail : TEXCOORD1; };
            struct V { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; float3 nWS : TEXCOORD1; float3 pWS : TEXCOORD2; float3 upWS : TEXCOORD3; float3 detail : TEXCOORD4; float3 nOS : TEXCOORD5; };
            V vert(A v)
            {
                V o;
                o.pWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.pWS);
                o.dir = v.dir; o.detail = v.detail; o.nOS = v.normalOS;
                o.nWS = TransformObjectToWorldNormal(v.normalOS);
                o.upWS = TransformObjectToWorldNormal(normalize(v.dir));
                return o;
            }

            void Tri(TEXTURE2D_PARAM(albedoTex, albedoSamp), TEXTURE2D_PARAM(norTex, norSamp), float3 p, float3 n, float tile, out float lum, out float3 nOut)
            {
                float3 w = pow(abs(n), 4); w /= (w.x + w.y + w.z);
                float2 ux = p.zy / tile, uy = p.xz / tile, uz = p.xy / tile;
                float3 a = SAMPLE_TEXTURE2D(albedoTex, albedoSamp, ux).rgb * w.x + SAMPLE_TEXTURE2D(albedoTex, albedoSamp, uy).rgb * w.y + SAMPLE_TEXTURE2D(albedoTex, albedoSamp, uz).rgb * w.z;
                lum = dot(a, float3(0.299, 0.587, 0.114));
                float3 nx = UnpackNormal(SAMPLE_TEXTURE2D(norTex, norSamp, ux)); float3 ny = UnpackNormal(SAMPLE_TEXTURE2D(norTex, norSamp, uy)); float3 nz = UnpackNormal(SAMPLE_TEXTURE2D(norTex, norSamp, uz));
                nx = float3(nx.xy + n.zy, abs(nx.z) * n.x); ny = float3(ny.xy + n.xz, abs(ny.z) * n.y); nz = float3(nz.xy + n.xy, abs(nz.z) * n.z);
                nOut = normalize(nx.zyx * w.x + ny.xzy * w.y + nz.xyz * w.z);
            }

            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 albedo = SAMPLE_TEXTURE2D(_ColorTex, sampler_ColorTex, KarmanEquirectUV(d)).rgb * _Albedo;
                float dist = length(_WorldSpaceCameraPos - i.pWS);
                float3 nOS = normalize(i.nOS);
                float nearW = 1 - smoothstep(40.0, 260.0, dist);
                float midW = 1 - smoothstep(800.0, 6000.0, dist);
                float lumN = 0.5, lumM = 0.5; float3 nN = nOS, nM = nOS;
                if (midW > 0.001) Tri(TEXTURE2D_ARGS(_MidDiff, sampler_MidDiff), TEXTURE2D_ARGS(_MidNor, sampler_MidNor), i.detail, nOS, 64.0, lumM, nM);
                if (nearW > 0.001) Tri(TEXTURE2D_ARGS(_NearDiff, sampler_NearDiff), TEXTURE2D_ARGS(_NearNor, sampler_NearNor), i.detail, nOS, 2.0, lumN, nN);
                // Detail modulates brightness around the LROC albedo (normalised to ~1 on average).
                float detail = lerp(1, lumM / 0.42, midW * 0.55) * lerp(1, lumN / 0.45, nearW * 0.6);
                albedo *= clamp(detail, 0.4, 1.8);
                float3 nDet = normalize(lerp(nOS, nM, midW * 0.8));
                nDet = normalize(lerp(nDet, nN, nearW));
                float3 N = normalize(TransformObjectToWorldNormal(nDet));
                float3 L = normalize(_SunDir.xyz);
                float3 Vv = normalize(_WorldSpaceCameraPos - i.pWS);
                float mu0 = saturate(dot(N, L));
                float mu = saturate(dot(N, Vv));
                float ls = 2.0 * mu0 / (mu0 + mu + 1e-4);
                float opposition = 1 + 0.25 * pow(saturate(dot(L, Vv)), 16);
                float horizon = smoothstep(-0.02, 0.06, dot(normalize(i.upWS), L) + 0.05);
                float shadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(i.pWS));
                float brdf = lerp(mu0, ls, 0.6) * opposition * horizon * shadow;
                float3 col = albedo * _SunIntensity * brdf;
                col += albedo * _EarthShine * saturate(dot(N, normalize(_EarthDir.xyz))) * float3(0.55, 0.65, 0.9);
                return half4(col, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Off
            HLSLPROGRAM
            #pragma vertex KShadowVert
            #pragma fragment KZeroFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _SunIntensity, _Albedo, _EarthShine;
                float4 _SunDir, _EarthDir;
            CBUFFER_END
            #include "KarmanCommonPasses.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Off
            HLSLPROGRAM
            #pragma vertex KDepthVert
            #pragma fragment KZeroFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _SunIntensity, _Albedo, _EarthShine;
                float4 _SunDir, _EarthDir;
            CBUFFER_END
            #include "KarmanCommonPasses.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On Cull Off
            HLSLPROGRAM
            #pragma vertex KDepthVert
            #pragma fragment KNormalFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _SunIntensity, _Albedo, _EarthShine;
                float4 _SunDir, _EarthDir;
            CBUFFER_END
            #include "KarmanCommonPasses.hlsl"
            ENDHLSL
        }
    }
}
