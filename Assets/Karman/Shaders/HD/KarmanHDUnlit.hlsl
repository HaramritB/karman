#ifndef KARMAN_HD_UNLIT
#define KARMAN_HD_UNLIT
// Drop-in replacement for URP's Core.hlsl in KÁRMÁN's self-lit shaders (Earth, Moon, planets, plumes, lines ...).
// HDRP renders camera-relative, so positions from TransformObjectToWorld are relative to the camera and the camera
// sits at the origin: _WorldSpaceCameraPos is redefined to zero so the original view-vector maths stays valid.
// Colours computed for the old unit sun (intensity ~3.2) are rescaled to HDRP's physical sun (lux) and exposure.
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariablesFunctions.hlsl"

float _KarmanUnlitScale;   // sun lux / 3.2, set by SpaceLighting
float KarmanExposure() { return GetCurrentExposureMultiplier() * (_KarmanUnlitScale > 0 ? _KarmanUnlitScale : 37500.0); }

#undef _WorldSpaceCameraPos
#define _WorldSpaceCameraPos float3(0, 0, 0)
#endif
