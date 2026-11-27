Shader "Karman/Earth"
{
    // Self-lit Earth globe. Outputs reflectance relative to a white Lambertian surface facing the Sun; the atmosphere
    // (limb, haze, aerial perspective) is added on top by HDRP's physically based sky.
    Properties
    {
        [NoScaleOffset] _DayTex ("Day (Blue Marble)", 2D) = "white" {}
        [NoScaleOffset] _NightTex ("Night lights", 2D) = "black" {}
        [NoScaleOffset] _CloudTex ("Clouds", 2D) = "black" {}
        [NoScaleOffset] _NormalTex ("Normal", 2D) = "bump" {}
        [NoScaleOffset] _SpecTex ("Ocean mask", 2D) = "black" {}
        [NoScaleOffset] _NoiseTex ("Tileable fBm noise (detail below the map resolution)", 2D) = "gray" {}
        _SunIntensity ("Sun intensity (1 = physical)", Float) = 1
        _NightIntensity ("City lights intensity", Float) = 0.9
        _NormalStrength ("Normal strength", Range(0,2)) = 0.6
        _CloudOffset ("Cloud longitude offset", Float) = 0
        _CloudShadow ("Cloud shadow strength", Range(0,1)) = 0.55
        _CloudHeight ("Cloud height / planet radius", Float) = 0.0012
        _CloudDetail ("Cloud edge detail", Range(0,2)) = 0.9
        _CloudRelief ("Cloud relief from the Sun side", Range(0,4)) = 1.6
        _LandDetail ("Land detail", Range(0,1)) = 0.35
        _SkyFill ("Skylight share of scattered sunlight", Range(0,1)) = 0.5
        _OceanColor ("Ocean tint", Color) = (0.02, 0.06, 0.12, 1)
        _ZenithOpticalDepth ("Zenith optical depth (RGB)", Vector) = (0.046, 0.108, 0.265, 0)
        _SunDir ("Sun direction (world)", Vector) = (1, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="HDRenderPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="ForwardOnly" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment KFrag
            #include "HD/KarmanHDUnlit.hlsl"
            #include "KarmanCommon.hlsl"

            TEXTURE2D(_DayTex); SAMPLER(sampler_DayTex);
            TEXTURE2D(_NightTex); SAMPLER(sampler_NightTex);
            TEXTURE2D(_CloudTex); SAMPLER(sampler_CloudTex);
            TEXTURE2D(_NormalTex); SAMPLER(sampler_NormalTex);
            TEXTURE2D(_SpecTex); SAMPLER(sampler_SpecTex);
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float _SunIntensity, _NightIntensity, _NormalStrength, _CloudOffset, _CloudShadow;
                float _CloudHeight, _CloudDetail, _CloudRelief, _LandDetail, _SkyFill;
                float4 _OceanColor, _ZenithOpticalDepth, _SunDir;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dirOS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.dirOS = normalize(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(o.dirOS);
                return o;
            }

            // Seamless noise on the sphere: the tileable texture projected along the three axes of the direction.
            float SphereNoise(float3 d, float tilesPerRadius)
            {
                float3 p = d * tilesPerRadius;
                float3 w = abs(d); w *= w; w *= w; w /= (w.x + w.y + w.z);
                return SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p.zy).r * w.x
                     + SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p.xz).r * w.y
                     + SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p.xy).r * w.z;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 dOS = normalize(i.dirOS);
                float2 uv = KarmanEquirectUV(dOS);
                float3 N = normalize(i.normalWS);
                float3 L = normalize(_SunDir.xyz);
                float3 V = normalize(_WorldSpaceCameraPos - i.positionWS);

                // Local tangent frame: east/north on the sphere, in world space.
                float3 poleWS = normalize(TransformObjectToWorldDir(float3(0, 1, 0)));
                float3 east = normalize(cross(poleWS, N) + 1e-6);
                float3 north = cross(N, east);
                float cosLat = max(sqrt(saturate(1.0 - dOS.y * dOS.y)), 0.05);
                // Converts a tangent-plane offset in radians of arc (east, north) to an equirectangular UV offset.
                float2 arcToUV = float2(1.0 / (2.0 * K_PI * cosLat), 1.0 / K_PI);

                // Structure below the 5 km texels of the global maps: two octaves of tileable noise, ~40 km and ~6 km
                // per tile. Mip-mapping averages it away as the globe recedes, so it needs no distance fade.
                float detail = SphereNoise(dOS, 160.0) * 0.6 + SphereNoise(dOS, 1070.0) * 0.4 - 0.5;

                float3 albedo = SAMPLE_TEXTURE2D(_DayTex, sampler_DayTex, uv).rgb;
                float ocean = SAMPLE_TEXTURE2D(_SpecTex, sampler_SpecTex, uv).r;
                float3 nT = UnpackNormal(SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, uv));
                nT.xy *= _NormalStrength * (1.0 - ocean);
                float3 Nd = normalize(east * nT.x + north * nT.y + N * nT.z);

                float NdotL = dot(N, L);
                float NdotV = saturate(dot(N, V));

                // Sunlight at the surface: the direct beam reddens with air mass, and part of what the air scatters out
                // of the beam still arrives as blue skylight, so daylight stays near white until the Sun is low.
                float3 T = KarmanSunTransmittance(NdotL, _ZenithOpticalDepth.xyz);
                float3 sunColor = (T + (1.0 - T) * _SkyFill) * _SunIntensity;

                // Clouds float _CloudHeight above the surface: seen through this pixel is the cloud above the point
                // shifted toward the viewer; the cloud shading this pixel is the one shifted toward the Sun.
                float2 guv = uv + float2(_CloudOffset, 0);
                float2 viewT = float2(dot(V, east), dot(V, north)) / max(NdotV, 0.3);
                float2 sunT = float2(dot(L, east), dot(L, north)) / max(NdotL, 0.08);
                float2 cuv = guv + viewT * _CloudHeight * arcToUV;
                float cloudMap = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, cuv).r;
                float cloudSunward = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, cuv + sunT * (_CloudHeight * 0.6) * arcToUV).r;
                float cloudAbove = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, guv + sunT * _CloudHeight * arcToUV).r;

                // Erode the soft edges of the cloud map with the detail noise, then firm the cover up a little.
                float edge = 4.0 * cloudMap * (1.0 - cloudMap);
                float cloud = saturate(cloudMap + detail * _CloudDetail * edge);
                cloud = smoothstep(0.03, 0.92, cloud);
                // Relief: thicker cloud toward the Sun shades this spot, thinner cloud lets it catch the light.
                float relief = clamp(1.0 + (cloudMap - cloudSunward) * _CloudRelief, 0.45, 1.25);

                float groundLight = 1.0 - _CloudShadow * smoothstep(0.05, 0.8, cloudAbove);
                float wrap = saturate((dot(Nd, L) + 0.05) / 1.05);

                // Land: albedo with sub-texel variation.
                float3 land = albedo * (1.0 + detail * 2.0 * _LandDetail);
                // Ocean: deep-water tint, sky reflected at grazing angles, and the Sun's glitter path. The glitter is
                // GGX on the sea-surface slope distribution, with roughness varying across wind patches.
                float3 water = albedo * 0.6 + _OceanColor.rgb * 0.4;
                float fresnelV = 0.02 + 0.98 * pow(1.0 - NdotV, 5.0);
                float3 skyReflection = float3(0.20, 0.30, 0.46) * fresnelV;
                float3 H = normalize(L + V);
                float NdotH = saturate(dot(N, H));
                float fresnelH = 0.02 + 0.98 * pow(1.0 - saturate(dot(V, H)), 5.0);
                float roughness = 0.20 + 0.16 * saturate(detail + 0.5);
                float glint = KarmanGGX(NdotH, roughness) * fresnelH * saturate(NdotL) * 0.25;

                float3 ground = lerp(land, water + skyReflection, ocean) * sunColor * wrap * groundLight;
                ground += ocean * glint * sunColor * groundLight;

                // Clouds: lit with a soft terminator; the tops sit above most of the air, so they redden later.
                float cloudLit = saturate((NdotL + 0.08) / 1.08);
                float3 Tcloud = KarmanSunTransmittance(NdotL, _ZenithOpticalDepth.xyz * 0.45);
                float3 cloudCol = (Tcloud + (1.0 - Tcloud) * _SkyFill) * _SunIntensity * cloudLit * 0.92 * relief;
                float3 col = lerp(ground, cloudCol, cloud);

                // City lights on the night side, dimmed by clouds.
                float night = 1.0 - smoothstep(-0.12, 0.06, NdotL);
                float3 lights = SAMPLE_TEXTURE2D(_NightTex, sampler_NightTex, uv).rgb;
                lights = pow(lights, 1.6) * float3(1.0, 0.82, 0.55);
                col += lights * night * (1.0 - cloud * 0.85) * _NightIntensity;

                return half4(col, 1);
            }
            float4 KFrag(Varyings i) : SV_Target { float4 c = frag(i); c.rgb = min(c.rgb * KarmanExposure(), 30000.0); return c; }
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
