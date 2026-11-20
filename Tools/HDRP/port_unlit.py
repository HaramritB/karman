import re, sys
DEPTH_PASS = '''
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
'''
for path in sys.argv[1:]:
    s = open(path).read()
    exposure = "Navball" not in path
    s = s.replace('"RenderPipeline"="UniversalPipeline"', '"RenderPipeline"="HDRenderPipeline"')
    s = s.replace('"LightMode"="UniversalForward"', '"LightMode"="ForwardOnly"')
    s = s.replace('#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"', '#include "HD/KarmanHDUnlit.hlsl"')
    if exposure and "#pragma fragment frag" in s:
        m = re.search(r'\bfrag\s*\(\s*(\w+)\s+(\w+)\s*\)', s)
        typ = m.group(1)
        s = s.replace("#pragma fragment frag", "#pragma fragment KFrag", 1)
        idx = s.index("ENDHLSL", m.end())
        s = s[:idx] + f"float4 KFrag({typ} i) : SV_Target {{ float4 c = frag(i); c.rgb = min(c.rgb * KarmanExposure(), 30000.0); return c; }}\n            " + s[idx:]
    opaque = '"Queue"="Geometry"' in s or ('"RenderType"="Opaque"' in s and "Transparent" not in s)
    if opaque and "DepthForwardOnly" not in s:
        # insert before the closing brace of the SubShader (second-to-last '}')
        last = s.rstrip().rfind("}")
        sub = s.rstrip()[:last].rfind("}")
        s = s[:sub] + DEPTH_PASS.lstrip("\n").rstrip() + "\n    " + s[sub:]
    open(path, "w").write(s)
    print("ported", path, "opaque" if opaque else "transparent")
