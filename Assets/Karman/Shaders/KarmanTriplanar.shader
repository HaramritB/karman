Shader "Karman/Triplanar"
{
    // PBR triplanar-mapped lit surface for procedural geometry without good UVs (pad, buildings, rocket parts).
    // World- or object-space projection, albedo tint, normal map, ARM (AO/rough/metal) map, URP main + additional
    // lights with shadows, and the standard ShadowCaster/DepthOnly/DepthNormals passes (for SSAO and atmosphere depth).
    Properties
    {
        _BaseColor ("Tint", Color) = (1,1,1,1)
        [NoScaleOffset] _BaseMap ("Albedo", 2D) = "white" {}
        [NoScaleOffset][Normal] _BumpMap ("Normal", 2D) = "bump" {}
        [NoScaleOffset] _ArmMap ("AO/Rough/Metal", 2D) = "white" {}
        _Tiling ("Metres per texture repeat", Float) = 4
        _NormalStrength ("Normal strength", Range(0, 2)) = 1
        _Metallic ("Metallic", Range(0,1)) = 0
        _Roughness ("Roughness scale", Range(0,2)) = 1
        _Desaturate ("Desaturate albedo", Range(0,1)) = 0
        _AlbedoStrength ("Albedo texture strength", Range(0,1)) = 1
        [Toggle] _ObjectSpace ("Object-space projection", Float) = 0
        [HideInInspector] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _FORWARD_PLUS _CLUSTER_LIGHT_LOOP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURE2D(_ArmMap); SAMPLER(sampler_ArmMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Tiling, _NormalStrength, _Metallic, _Roughness, _Desaturate, _AlbedoStrength, _ObjectSpace, _Cull;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 projPos : TEXCOORD2; float3 projNormal : TEXCOORD3; };

            V vert(A v)
            {
                V o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                // Object-space projection keeps textures attached to moving parts; world space suits static scenery.
                o.projPos = _ObjectSpace > 0.5 ? v.positionOS.xyz : o.positionWS;
                o.projNormal = _ObjectSpace > 0.5 ? v.normalOS : o.normalWS;
                return o;
            }

            // Whiteout-blended triplanar normal mapping (Ben Golus).
            void Triplanar(float3 p, float3 n, out float3 albedo, out float3 arm, out float3 tnormal)
            {
                float3 w = pow(abs(n), 4); w /= (w.x + w.y + w.z);
                float2 uvX = p.zy / _Tiling, uvY = p.xz / _Tiling, uvZ = p.xy / _Tiling;
                albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvX).rgb * w.x + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvY).rgb * w.y + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvZ).rgb * w.z;
                arm = SAMPLE_TEXTURE2D(_ArmMap, sampler_ArmMap, uvX).rgb * w.x + SAMPLE_TEXTURE2D(_ArmMap, sampler_ArmMap, uvY).rgb * w.y + SAMPLE_TEXTURE2D(_ArmMap, sampler_ArmMap, uvZ).rgb * w.z;
                float3 nX = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvX), _NormalStrength);
                float3 nY = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvY), _NormalStrength);
                float3 nZ = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvZ), _NormalStrength);
                nX = float3(nX.xy + n.zy, abs(nX.z) * n.x);
                nY = float3(nY.xy + n.xz, abs(nY.z) * n.y);
                nZ = float3(nZ.xy + n.xy, abs(nZ.z) * n.z);
                tnormal = normalize(nX.zyx * w.x + nY.xzy * w.y + nZ.xyz * w.z);
            }

            half4 frag(V i) : SV_Target
            {
                float3 nProj = normalize(i.projNormal);
                float3 albedoT, arm, nP;
                Triplanar(i.projPos, nProj, albedoT, arm, nP);
                float3 N = _ObjectSpace > 0.5 ? normalize(TransformObjectToWorldNormal(nP)) : nP;
                float lum = dot(albedoT, float3(0.299, 0.587, 0.114));
                albedoT = lerp(albedoT, lum.xxx, _Desaturate);
                float3 albedo = lerp(1.0.xxx, albedoT, _AlbedoStrength) * _BaseColor.rgb;

                InputData input = (InputData)0;
                input.positionWS = i.positionWS;
                input.normalWS = N;
                input.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                input.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                input.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                input.bakedGI = SampleSH(N);
                input.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surf = (SurfaceData)0;
                surf.albedo = albedo;
                surf.metallic = _Metallic;
                surf.smoothness = saturate(1 - arm.g * _Roughness);
                surf.occlusion = arm.r;
                surf.normalTS = float3(0, 0, 1);
                surf.alpha = 1;
                half4 c = UniversalFragmentPBR(input, surf);
                return half4(c.rgb, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma vertex KShadowVert
            #pragma fragment KZeroFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Tiling, _NormalStrength, _Metallic, _Roughness, _Desaturate, _AlbedoStrength, _ObjectSpace, _Cull;
            CBUFFER_END
            #include "KarmanCommonPasses.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma vertex KDepthVert
            #pragma fragment KZeroFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Tiling, _NormalStrength, _Metallic, _Roughness, _Desaturate, _AlbedoStrength, _ObjectSpace, _Cull;
            CBUFFER_END
            #include "KarmanCommonPasses.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma vertex KDepthVert
            #pragma fragment KNormalFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Tiling, _NormalStrength, _Metallic, _Roughness, _Desaturate, _AlbedoStrength, _ObjectSpace, _Cull;
            CBUFFER_END
            #include "KarmanCommonPasses.hlsl"
            ENDHLSL
        }
    }
}
