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
        _HazeColor ("Aerial perspective colour", Color) = (0.58, 0.70, 0.86, 1)
        _HazeDistance ("Haze e-folding distance (m)", Float) = 16000
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
                float _NightIntensity, _HazeDistance;
                float4 _SunDir, _SkyColor, _HazeColor;
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


            // Inigo Quilez's noise-indexed double sampling: hides the repeat of tiling textures. Returns the blend
            // weight and offsets so the albedo, ARM and normal maps stay in register.
            void NoTile(float2 uv, out float2 oa, out float2 ob, out float t)
            {
                float l = noise(uv * 0.19) * 8.0;
                float ia = floor(l); t = smoothstep(0.25, 0.75, frac(l));
                oa = sin(float2(3.0, 7.0) * ia); ob = sin(float2(3.0, 7.0) * (ia + 1));
            }
            float3 Tex3(TEXTURE2D_PARAM(tx, ss), float2 uv, float2 oa, float2 ob, float t)
            {
                float2 dx = ddx(uv), dy = ddy(uv);
                return lerp(SAMPLE_TEXTURE2D_GRAD(tx, ss, uv + oa, dx, dy).rgb, SAMPLE_TEXTURE2D_GRAD(tx, ss, uv + ob, dx, dy).rgb, t);
            }
            float4 Tex4(TEXTURE2D_PARAM(tx, ss), float2 uv, float2 oa, float2 ob, float t)
            {
                float2 dx = ddx(uv), dy = ddy(uv);
                return lerp(SAMPLE_TEXTURE2D_GRAD(tx, ss, uv + oa, dx, dy), SAMPLE_TEXTURE2D_GRAD(tx, ss, uv + ob, dx, dy), t);
            }
            float Lum(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }
            // Distance to an axis-aligned rectangle outline (perimeter road).
            float RectRing(float2 p, float2 halfSize) { float2 d = abs(p) - halfSize; return abs(length(max(d, 0)) + min(max(d.x, d.y), 0)); }

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

                // Grass detail at two scales (3 m and 13 m), both anti-tiled, normalised to their mean colour so a
                // procedural palette sets the hue and the photo-scan supplies the texture.
                float2 o1a, o1b, o2a, o2b; float t1, t2;
                float2 uv1 = p / 3.0, uv2 = p / 13.0;
                NoTile(uv1, o1a, o1b, t1); NoTile(uv2 + 7.3, o2a, o2b, t2);
                float3 g1 = Tex3(TEXTURE2D_ARGS(_GrassDiff, sampler_GrassDiff), uv1, o1a, o1b, t1);
                float3 g2 = Tex3(TEXTURE2D_ARGS(_GrassDiff, sampler_GrassDiff), uv2, o2a, o2b, t2);
                float3 gMean = SAMPLE_TEXTURE2D_LOD(_GrassDiff, sampler_GrassDiff, float2(0.5, 0.5), 12).rgb;
                float nearW = 1 - smoothstep(80.0, 900.0, dist);
                float3 detail = lerp(g2, lerp(g1, g2, 0.35), nearW) / max(gMean, 0.02);
                detail = lerp(1.0.xxx, detail, 0.85);
                float3 armG = Tex3(TEXTURE2D_ARGS(_GrassArm, sampler_GrassArm), uv1, o1a, o1b, t1);
                float3 nG = UnpackNormal(Tex4(TEXTURE2D_ARGS(_GrassNor, sampler_GrassNor), uv1, o1a, o1b, t1));

                // Macro breakup from the aerial scan (luminance only) plus procedural vegetation patches.
                float2 o3a, o3b; float t3; float2 uv3 = p / 140.0; NoTile(uv3, o3a, o3b, t3);
                float3 macro = Tex3(TEXTURE2D_ARGS(_MacroDiff, sampler_MacroDiff), uv3, o3a, o3b, t3);
                float3 mMean = SAMPLE_TEXTURE2D_LOD(_MacroDiff, sampler_MacroDiff, float2(0.5, 0.5), 12).rgb;
                float macroL = clamp(Lum(macro) / max(Lum(mMean), 0.02), 0.6, 1.45);
                float patchA = fbm(p / 260.0), patchB = fbm(p / 45.0 + 19.7), shrubs = fbm(p / 9.0 + 3.3);
                // Florida coastal scrub palette (linear albedo): saw palmetto/scrub oak, dry grass, mown lawn near the pad.
                float3 scrub = float3(0.075, 0.095, 0.045), dryGrass = float3(0.20, 0.18, 0.10), lawn = float3(0.065, 0.085, 0.04);
                float3 col = lerp(scrub, dryGrass, smoothstep(0.45, 0.72, patchA) * 0.7);
                col = lerp(col, col * 0.55, smoothstep(0.5, 0.7, shrubs) * (1 - smoothstep(0.55, 0.75, patchA)));   // dark shrub clumps
                col *= lerp(0.85, 1.15, patchB);
                float lawnMask = 1 - smoothstep(170.0, 230.0, max(abs(p.x) - 20.0, abs(p.y) + 10.0));
                lawnMask = max(lawnMask, (1 - smoothstep(22.0, 40.0, abs(p.x))) * step(p.y, -100.0) * (1 - smoothstep(2400.0, 2600.0, -p.y))); // crawlerway verge
                col = lerp(col, lawn, lawnMask);
                col *= detail * lerp(1.0, macroL, 0.7 * (1 - lawnMask * 0.6));

                // Perimeter road around the pad (asphalt, 8 m wide) and gravel shoulder.
                float road = 1 - smoothstep(3.6, 4.4, RectRing(p - float2(0, -6), float2(122, 116)));
                float shoulder = (1 - smoothstep(5.0, 6.5, RectRing(p - float2(0, -6), float2(122, 116)))) * (1 - road);
                float asphaltN = noise(p * 1.7) * 0.5 + noise(p * 7.0) * 0.5;
                col = lerp(col, float3(0.07, 0.07, 0.068) * (0.85 + 0.3 * asphaltN), road);
                col = lerp(col, float3(0.24, 0.23, 0.21) * (0.8 + 0.4 * asphaltN), shoulder);

                float3 globalCol = SAMPLE_TEXTURE2D(_DayTex, sampler_DayTex, i.uv).rgb;
                float detailFade = 1 - smoothstep(4000.0, 15000.0, length(p));
                float3 land = lerp(globalCol, col, detailFade * localWeight);
                float3 sand = SAMPLE_TEXTURE2D(_SandDiff, sampler_SandDiff, p / 3.0).rgb;
                float3 sandN = UnpackNormal(SAMPLE_TEXTURE2D(_SandNor, sampler_SandNor, p / 3.0));
                land = lerp(land, sand * float3(0.95, 0.9, 0.8), beach * localWeight);
                float flatten = road + shoulder;
                float3 tN = lerp(float3(0, 0, 1), lerp(nG, sandN, beach), nearW * (1 - ocean) * (1 - flatten));
                float3 N = normalize(i.eastWS * tN.x + i.northWS * tN.y + Ngeo * tN.z);

                // Water: wind-rippled normal, dark deep-blue body, sky reflection by Fresnel.
                float tm = _Time.y;
                float2 w1 = float2(noise(p / 6.0 + tm * 0.3), noise(p / 6.0 + 17.0 - tm * 0.25)) - 0.5;
                float3 Nwater = normalize(Ngeo + (i.eastWS * w1.x + i.northWS * w1.y) * 0.25);

                InputData input = (InputData)0;
                input.positionWS = i.positionWS;
                input.normalWS = lerp(N, Nwater, ocean);
                input.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                input.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                input.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                float sunUp = dot(Ngeo, normalize(_SunDir.xyz));
                float dayAmb = saturate(sunUp * 3 + 0.15);
                input.bakedGI = _SkyColor.rgb * 0.45 * dayAmb;
                input.shadowMask = half4(1, 1, 1, 1);
                SurfaceData s = (SurfaceData)0;
                s.albedo = lerp(land, float3(0.02, 0.05, 0.07), ocean);
                float rough = lerp(lerp(armG.g, 0.9, flatten), 0.8, beach);
                s.smoothness = lerp(lerp(1 - rough, 0.08, 1 - nearW), 0.92, ocean);
                s.occlusion = lerp(1, armG.r, nearW * (1 - ocean) * (1 - flatten));
                s.normalTS = float3(0, 0, 1); s.alpha = 1;
                float3 c = UniversalFragmentPBR(input, s).rgb;
                float fres = pow(1 - saturate(dot(input.normalWS, input.viewDirectionWS)), 5) * 0.9 + 0.04;
                float day = saturate(sunUp * 4 + 0.2);
                c += ocean * fres * _SkyColor.rgb * 1.6 * day;
                float night = 1 - smoothstep(-0.12, 0.06, sunUp);
                c += pow(SAMPLE_TEXTURE2D(_NightTex, sampler_NightTex, i.uv).rgb, 1.6) * float3(1, 0.82, 0.55) * night * _NightIntensity * (1 - nearW);
                // Aerial perspective: distant ground fades into the lit horizon haze.
                Light sunL = GetMainLight();
                float haze = 1 - exp(-dist / _HazeDistance);
                c = lerp(c, _HazeColor.rgb * sunL.color * 0.33 * day, haze * 0.85);
                return half4(c, 1);
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
                float _NightIntensity, _HazeDistance;
                float4 _SunDir, _SkyColor, _HazeColor;
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
                float _NightIntensity, _HazeDistance;
                float4 _SunDir, _SkyColor, _HazeColor;
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
                float _NightIntensity, _HazeDistance;
                float4 _SunDir, _SkyColor, _HazeColor;
            CBUFFER_END
            #include "KarmanCommonPasses.hlsl"
            ENDHLSL
        }
    }
}
