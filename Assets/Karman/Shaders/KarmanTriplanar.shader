Shader "Karman/Triplanar"
{
    // PBR triplanar-mapped lit surface for procedural geometry without good UVs (pad, buildings, rocket parts).
    // Projection space: 0 = world (static scenery in the VAB), 1 = object (moving parts), 2 = launch-site frame
    // (global _KarmanSiteW2L, so pad textures stay fixed under the floating origin and Earth rotation).
    // Albedo tint, anti-tiling, normal map, ARM (AO/rough/metal) map, low-frequency grime/weathering, URP main +
    // additional lights with shadows and SSAO, plus ShadowCaster/DepthOnly/DepthNormals passes.
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
        _ObjectSpace ("Projection space (0 world, 1 object, 2 site)", Float) = 0
        _Grime ("Weathering amount", Range(0,1)) = 0
        _GrimeScale ("Weathering scale (m)", Float) = 6
        _GrimeColor ("Weathering colour", Color) = (0.32, 0.3, 0.27, 1)
        _Panel ("Slab / panel size (m, 0 = off)", Float) = 0
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
                float4 _BaseColor, _GrimeColor;
                float _Tiling, _NormalStrength, _Metallic, _Roughness, _Desaturate, _AlbedoStrength, _ObjectSpace, _Cull, _Grime, _GrimeScale, _Panel;
            CBUFFER_END
            float4x4 _KarmanSiteW2L;

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 projPos : TEXCOORD2; float3 projNormal : TEXCOORD3; };

            float hash31(float3 p) { p = frac(p * 0.1031); p += dot(p, p.zyx + 31.32); return frac((p.x + p.y) * p.z); }
            float vnoise(float3 p)
            {
                float3 i = floor(p), f = frac(p); float3 u = f * f * (3 - 2 * f);
                return lerp(lerp(lerp(hash31(i), hash31(i + float3(1,0,0)), u.x), lerp(hash31(i + float3(0,1,0)), hash31(i + float3(1,1,0)), u.x), u.y),
                            lerp(lerp(hash31(i + float3(0,0,1)), hash31(i + float3(1,0,1)), u.x), lerp(hash31(i + float3(0,1,1)), hash31(i + float3(1,1,1)), u.x), u.y), u.z);
            }

            V vert(A v)
            {
                V o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                if (_ObjectSpace > 1.5)
                {
                    o.projPos = mul(_KarmanSiteW2L, float4(o.positionWS, 1)).xyz;
                    o.projNormal = mul((float3x3)_KarmanSiteW2L, o.normalWS);
                }
                else if (_ObjectSpace > 0.5) { o.projPos = v.positionOS.xyz; o.projNormal = v.normalOS; }
                else { o.projPos = o.positionWS; o.projNormal = o.normalWS; }
                return o;
            }

            // Whiteout-blended triplanar normal mapping (Ben Golus). The top projection (floors, decks) uses
            // Inigo Quilez's noise-indexed double sampling so the repeat pattern never lines up.
            void Triplanar(float3 p, float3 n, out float3 albedo, out float3 arm, out float3 tnormal)
            {
                float3 w = pow(abs(n), 4); w /= (w.x + w.y + w.z);
                float2 uvX = p.zy / _Tiling, uvY = p.xz / _Tiling, uvZ = p.xy / _Tiling;
                float l = vnoise(float3(uvY * 0.21, 0.5)) * 8.0;
                float ia = floor(l), fl = frac(l);
                float2 oa = sin(float2(3.0, 7.0) * ia), ob = sin(float2(3.0, 7.0) * (ia + 1));
                float2 dx = ddx(uvY), dy = ddy(uvY);
                float3 ya = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, uvY + oa, dx, dy).rgb;
                float3 yb = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, uvY + ob, dx, dy).rgb;
                float tb = smoothstep(0.2, 0.8, fl - 0.1 * dot(ya - yb, 1));
                float3 albY = lerp(ya, yb, tb);
                float3 armY = lerp(SAMPLE_TEXTURE2D_GRAD(_ArmMap, sampler_ArmMap, uvY + oa, dx, dy).rgb, SAMPLE_TEXTURE2D_GRAD(_ArmMap, sampler_ArmMap, uvY + ob, dx, dy).rgb, tb);
                float3 nY = lerp(UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(_BumpMap, sampler_BumpMap, uvY + oa, dx, dy), _NormalStrength),
                                 UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(_BumpMap, sampler_BumpMap, uvY + ob, dx, dy), _NormalStrength), tb);
                albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvX).rgb * w.x + albY * w.y + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvZ).rgb * w.z;
                arm = SAMPLE_TEXTURE2D(_ArmMap, sampler_ArmMap, uvX).rgb * w.x + armY * w.y + SAMPLE_TEXTURE2D(_ArmMap, sampler_ArmMap, uvZ).rgb * w.z;
                float3 nX = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvX), _NormalStrength);
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
                float3 N;
                if (_ObjectSpace > 1.5) N = normalize(mul(transpose((float3x3)_KarmanSiteW2L), nP));
                else if (_ObjectSpace > 0.5) N = normalize(TransformObjectToWorldNormal(nP));
                else N = nP;
                float lum = dot(albedoT, float3(0.299, 0.587, 0.114));
                albedoT = lerp(albedoT, lum.xxx, _Desaturate);
                float3 albedo = lerp(1.0.xxx, albedoT, _AlbedoStrength) * _BaseColor.rgb;
                float rough = arm.g * _Roughness;

                // Weathering: large soft stains plus streaks running down vertical faces.
                if (_Grime > 0.001)
                {
                    float3 q = i.projPos / _GrimeScale;
                    float stain = vnoise(q) * 0.6 + vnoise(q * 2.7 + 11.0) * 0.3 + vnoise(q * 7.3 + 5.0) * 0.1;
                    float streak = vnoise(float3(q.x * 6.0, q.y * 0.35, q.z * 6.0)) * (1 - abs(nProj.y));
                    float g = saturate((stain - 0.42) * 2.5 + streak * 0.6) * _Grime;
                    albedo = lerp(albedo, albedo * _GrimeColor.rgb * 1.6, g);
                    rough = saturate(rough + g * 0.25);
                }

                // Concrete slabs / deck plates on upward faces: per-slab tone variation and dark expansion joints.
                if (_Panel > 0.01 && nProj.y > 0.7)
                {
                    float2 q = i.projPos.xz / _Panel;
                    float2 cell = floor(q), f = frac(q);
                    float tone = hash31(float3(cell, 3.7));
                    albedo *= 0.86 + 0.24 * tone;
                    float edge = min(min(f.x, 1 - f.x), min(f.y, 1 - f.y)) * _Panel;   // metres to the nearest joint
                    float w = fwidth(edge) + 1e-4;
                    float joint = (1 - smoothstep(0.025, 0.025 + w, edge)) * saturate(0.08 / w);
                    albedo *= 1 - 0.55 * joint;
                    rough = lerp(rough, 1, joint);
                }

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
                surf.smoothness = saturate(1 - rough);
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
                float4 _BaseColor, _GrimeColor;
                float _Tiling, _NormalStrength, _Metallic, _Roughness, _Desaturate, _AlbedoStrength, _ObjectSpace, _Cull, _Grime, _GrimeScale, _Panel;
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
                float4 _BaseColor, _GrimeColor;
                float _Tiling, _NormalStrength, _Metallic, _Roughness, _Desaturate, _AlbedoStrength, _ObjectSpace, _Cull, _Grime, _GrimeScale, _Panel;
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
                float4 _BaseColor, _GrimeColor;
                float _Tiling, _NormalStrength, _Metallic, _Roughness, _Desaturate, _AlbedoStrength, _ObjectSpace, _Cull, _Grime, _GrimeScale, _Panel;
            CBUFFER_END
            #include "KarmanCommonPasses.hlsl"
            ENDHLSL
        }
    }
}
