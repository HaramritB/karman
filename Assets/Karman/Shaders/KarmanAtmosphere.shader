Shader "Karman/Atmosphere"
{
    // Single-scattering Rayleigh + Mie atmosphere, ray-marched per pixel in planet-radius units.
    // Ray end is the nearest of: atmosphere exit, planet surface, scene depth (vessels, moons).
    Properties
    {
        _SunIntensity ("Sun intensity", Float) = 22
        _BetaR ("Rayleigh β per planet radius", Vector) = (36.95, 86.0, 210.9, 0)
        _BetaM ("Mie β per planet radius", Float) = 133.8
        _ScaleHeightR ("Rayleigh scale height / R", Float) = 0.001256
        _ScaleHeightM ("Mie scale height / R", Float) = 0.000188
        _MieG ("Mie anisotropy g", Range(0, 0.99)) = 0.76
        _AtmoTop ("Atmosphere top / R", Float) = 1.0157
        _PlanetCenter ("Planet centre (world)", Vector) = (0,0,0,0)
        _PlanetRadius ("Planet radius (world)", Float) = 1
        _SunDir ("Sun direction (world)", Vector) = (1,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-10" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Atmosphere"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "KarmanCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _SunIntensity, _BetaM, _ScaleHeightR, _ScaleHeightM, _MieG, _AtmoTop, _PlanetRadius;
                float4 _BetaR, _PlanetCenter, _SunDir;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            float _KarmanAtmoSteps;   // global, set by the quality preset (0 = default 32)
            #define LIGHT_STEPS 6

            half4 frag(Varyings i) : SV_Target
            {
                float3 cam = _WorldSpaceCameraPos;
                float3 rd = normalize(i.positionWS - cam);
                float3 ro = (cam - _PlanetCenter.xyz) / _PlanetRadius;
                float3 L = normalize(_SunDir.xyz);

                float2 tA = KarmanRaySphere(ro, rd, 0, _AtmoTop);
                if (tA.y < 0.0 || tA.x > tA.y) return 0;
                float tStart = max(tA.x, 0.0);
                float tEnd = tA.y;
                float2 tP = KarmanRaySphere(ro, rd, 0, 1.0);
                if (tP.x > 0.0 && tP.x <= tP.y) tEnd = min(tEnd, tP.x);

                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float raw = SampleSceneDepth(suv);
                #if UNITY_REVERSED_Z
                    bool far = raw <= 1e-7;
                #else
                    bool far = raw >= 1.0 - 1e-7;
                #endif
                if (!far)
                {
                    float3 camFwd = -UNITY_MATRIX_V[2].xyz;
                    float sceneT = LinearEyeDepth(raw, _ZBufferParams) / max(dot(rd, camFwd), 1e-4) / _PlanetRadius;
                    tEnd = min(tEnd, sceneT);
                }
                if (tEnd <= tStart) return 0;

                int steps = _KarmanAtmoSteps > 0.5 ? (int)_KarmanAtmoSteps : 32;
                float ds = (tEnd - tStart) / steps;
                float odR = 0, odM = 0;
                float3 sumR = 0, sumM = 0;
                float3 betaR = _BetaR.xyz;
                float betaMExt = _BetaM * 1.1;

                [loop] for (int s = 0; s < steps; s++)
                {
                    float3 p = ro + rd * (tStart + (s + 0.5) * ds);
                    float h = length(p) - 1.0;
                    float dR = exp(-h / _ScaleHeightR) * ds;
                    float dM = exp(-h / _ScaleHeightM) * ds;
                    odR += dR; odM += dM;

                    float2 tl = KarmanRaySphere(p, L, 0, _AtmoTop);
                    float2 tg = KarmanRaySphere(p, L, 0, 1.0);
                    if (tg.x > 0.0 && tg.x <= tg.y) continue; // in the planet's shadow
                    float lds = tl.y / LIGHT_STEPS;
                    float odLR = 0, odLM = 0;
                    [loop] for (int j = 0; j < LIGHT_STEPS; j++)
                    {
                        float hq = length(p + L * (j + 0.5) * lds) - 1.0;
                        odLR += exp(-hq / _ScaleHeightR) * lds;
                        odLM += exp(-hq / _ScaleHeightM) * lds;
                    }
                    float3 att = exp(-(betaR * (odR + odLR) + betaMExt * (odM + odLM)));
                    sumR += att * dR;
                    sumM += att * dM;
                }

                float mu = dot(rd, L);
                float phaseR = 3.0 / (16.0 * K_PI) * (1.0 + mu * mu);
                float g = _MieG, g2 = g * g;
                float phaseM = 3.0 / (8.0 * K_PI) * ((1.0 - g2) * (1.0 + mu * mu)) / ((2.0 + g2) * pow(max(1.0 + g2 - 2.0 * g * mu, 1e-4), 1.5));
                float3 inscatter = _SunIntensity * (sumR * betaR * phaseR + sumM * _BetaM * phaseM);
                float3 trans = exp(-(betaR * odR + betaMExt * odM));
                float alpha = 1.0 - dot(trans, float3(0.3333, 0.3333, 0.3334));
                return half4(inscatter, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
