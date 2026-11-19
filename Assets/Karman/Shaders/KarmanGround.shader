Shader "Karman/Ground"
{
    // Launch-site ground: PBR splat of scrub grass (near + macro variation), beach sand and ocean along a local
    // coastline, fading into the global Earth imagery at the patch rim. Lit by URP (sun + shadows + ambient), so the
    // pad, rocket and terrain share one lighting model.
    Properties
    {
        [NoScaleOffset] _DayTex ("Global day", 2D) = "white" {}
        [NoScaleOffset] _NightTex ("Night", 2D) = "black" {}
        [NoScaleOffset] _SpecTex ("Ocean mask", 2D) = "black" {}
        [NoScaleOffset] _GrassDiff ("Grass albedo", 2D) = "white" {}
        [NoScaleOffset][Normal] _GrassNor ("Grass normal", 2D) = "bump" {}
        [NoScaleOffset] _GrassArm ("Grass ARM", 2D) = "white" {}
        [NoScaleOffset] _MacroDiff ("Aerial macro albedo", 2D) = "white" {}
        [NoScaleOffset] _SandDiff ("Sand albedo", 2D) = "white" {}
        [NoScaleOffset][Normal] _SandNor ("Sand normal", 2D) = "bump" {}
        _NightIntensity ("City lights", Float) = 2.5
        _SunDir ("Sun direction (world)", Vector) = (0,1,0,0)
        _SkyColor ("Sky reflection colour", Color) = (0.32, 0.5, 0.78, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry-1" }
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
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS _CLUSTER_LIGHT_LOOP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_DayTex); SAMPLER(sampler_DayTex);
            TEXTURE2D(_NightTex); SAMPLER(sampler_NightTex);
            TEXTURE2D(_SpecTex); SAMPLER(sampler_SpecTex);
            TEXTURE2D(_GrassDiff); SAMPLER(sampler_GrassDiff);
            TEXTURE2D(_GrassNor); SAMPLER(sampler_GrassNor);
            TEXTURE2D(_GrassArm); SAMPLER(sampler_GrassArm);
            TEXTURE2D(_MacroDiff); SAMPLER(sampler_MacroDiff);
            TEXTURE2D(_SandDiff); SAMPLER(sampler_SandDiff);
            TEXTURE2D(_SandNor); SAMPLER(sampler_SandNor);
            CBUFFER_START(UnityPerMaterial)
                float _NightIntensity;
                float4 _SunDir, _SkyColor;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float rim : TEXCOORD1; float3 normalWS : TEXCOORD2; float3 positionWS : TEXCOORD3; float2 local : TEXCOORD4; float3 eastWS : TEXCOORD5; float3 northWS : TEXCOORD6; };

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise(float2 p) { float2 i = floor(p), f = frac(p); float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), u.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), u.x), u.y); }
            float fbm(float2 p) { float a = 0.5, s = 0; for (int k = 0; k < 5; k++) { s += a * noise(p); p *= 2.03; a *= 0.5; } return s; }

            V vert(A v)
            {
                V o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = v.uv; o.rim = v.uv2.x;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.local = v.positionOS.xz;
                o.eastWS = TransformObjectToWorldDir(float3(1, 0, 0));
                o.northWS = TransformObjectToWorldDir(float3(0, 0, 1));
                return o;
            }


            half4 frag(V i) : SV_Target
            {
                float3 Ngeo = normalize(i.normalWS);
                float dist = length(_WorldSpaceCameraPos - i.positionWS);
                float2 p = i.local;
                // Coastline (Atlantic shore ~1.4 km east of LC-39A, NNW-trending) with a sand beach.
                float wiggle = (fbm(float2(p.y / 900.0, 3.1)) - 0.5) * 260.0;
                float shoreX = 1400.0 + p.y * 0.18 + wiggle;
                float localOcean = smoothstep(shoreX - 8.0, shoreX + 8.0, p.x);
                float beach = smoothstep(shoreX - 160.0, shoreX - 70.0, p.x) * (1 - localOcean);
                float localWeight = 1 - smoothstep(12000.0, 18000.0, length(p));
                float globalOcean = SAMPLE_TEXTURE2D(_SpecTex, sampler_SpecTex, i.uv).r;
                float ocean = lerp(globalOcean, localOcean, localWeight);

                // Detail layers: near tiling (2.5 m) and macro aerial tiling (60 m), blended by distance.
                float2 uvNear = p / 2.5, uvMacro = p / 60.0, uvSand = p / 3.0;
                float3 grass = SAMPLE_TEXTURE2D(_GrassDiff, sampler_GrassDiff, uvNear).rgb;
                float3 grassArm = SAMPLE_TEXTURE2D(_GrassArm, sampler_GrassArm, uvNear).rgb;
                float3 grassN = UnpackNormal(SAMPLE_TEXTURE2D(_GrassNor, sampler_GrassNor, uvNear));
                float3 macro = SAMPLE_TEXTURE2D(_MacroDiff, sampler_MacroDiff, uvMacro).rgb;
                float3 sand = SAMPLE_TEXTURE2D(_SandDiff, sampler_SandDiff, uvSand).rgb;
                float3 sandN = UnpackNormal(SAMPLE_TEXTURE2D(_SandNor, sampler_SandNor, uvSand));
                float nearW = 1 - smoothstep(60.0, 450.0, dist);
                float variation = fbm(p / 180.0);
                // Florida coastal scrub: grade the grass toward olive/brown with large-scale patchiness.
                float3 scrub = lerp(grass, macro * float3(0.85, 0.95, 0.75), 1 - nearW) * lerp(float3(0.75, 0.82, 0.62), float3(1.0, 0.95, 0.8), variation);
                float3 globalCol = SAMPLE_TEXTURE2D(_DayTex, sampler_DayTex, i.uv).rgb;
                float detailFade = 1 - smoothstep(3000.0, 14000.0, length(p));
                float3 land = lerp(globalCol, scrub, detailFade * localWeight);
                land = lerp(land, sand * float3(1.05, 1.0, 0.9), beach * localWeight);
                float3 tN = lerp(float3(0, 0, 1), lerp(grassN, sandN, beach), nearW * (1 - ocean));
                float3 N = normalize(i.eastWS * tN.x + i.northWS * tN.y + Ngeo * tN.z);

                // Water: wind-rippled normal, dark deep-blue body, sky reflection by Fresnel.
                float t = _Time.y;
                float2 w1 = float2(noise(p / 6.0 + t * 0.3), noise(p / 6.0 + 17.0 - t * 0.25)) - 0.5;
                float3 Nwater = normalize(Ngeo + (i.eastWS * w1.x + i.northWS * w1.y) * 0.25);

                InputData input = (InputData)0;
                input.positionWS = i.positionWS;
                input.normalWS = lerp(N, Nwater, ocean);
                input.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                input.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                input.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                input.bakedGI = _SkyColor.rgb * 0.35;
                input.shadowMask = half4(1, 1, 1, 1);
                SurfaceData s = (SurfaceData)0;
                s.albedo = lerp(land, float3(0.02, 0.05, 0.07), ocean);
                s.smoothness = lerp(lerp(1 - grassArm.g, 0.25, beach) * nearW + 0.12 * (1 - nearW), 0.92, ocean);
                s.occlusion = lerp(1, grassArm.r, nearW * (1 - ocean));
                s.normalTS = float3(0, 0, 1); s.alpha = 1;
                float3 col = UniversalFragmentPBR(input, s).rgb;
                float fres = pow(1 - saturate(dot(input.normalWS, input.viewDirectionWS)), 5) * 0.9 + 0.04;
                float day = saturate(dot(Ngeo, normalize(_SunDir.xyz)) * 4 + 0.2);
                col += ocean * fres * _SkyColor.rgb * 1.6 * day;
                float night = 1 - smoothstep(-0.12, 0.06, dot(Ngeo, normalize(_SunDir.xyz)));
                col += pow(SAMPLE_TEXTURE2D(_NightTex, sampler_NightTex, i.uv).rgb, 1.6) * float3(1, 0.82, 0.55) * night * _NightIntensity * (1 - nearW);
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
                float _NightIntensity;
                float4 _SunDir, _SkyColor;
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
                float _NightIntensity;
                float4 _SunDir, _SkyColor;
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
                float _NightIntensity;
                float4 _SunDir, _SkyColor;
            CBUFFER_END
            #include "KarmanCommonPasses.hlsl"
            ENDHLSL
        }
    }
}
