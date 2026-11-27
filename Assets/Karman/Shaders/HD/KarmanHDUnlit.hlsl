#ifndef KARMAN_HD_UNLIT
#define KARMAN_HD_UNLIT
// Drop-in replacement for URP's Core.hlsl in KÁRMÁN's self-lit shaders (Earth, Moon, planets, plumes, lines ...).
// HDRP renders camera-relative, so positions from TransformObjectToWorld are relative to the camera and the camera
// sits at the origin: _WorldSpaceCameraPos is redefined to zero so the original view-vector maths stays valid.
// Photometry: these shaders compute reflectance relative to a white Lambertian surface facing the Sun (1 = that
// surface). KarmanExposure() converts it to pre-exposed luminance: E_sun / π nits times the camera exposure.
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariablesFunctions.hlsl"

float _KarmanUnlitScale;   // E_sun / π in nits (120,000 lx → 38,197), set by SpaceLighting
float KarmanExposure() { return GetCurrentExposureMultiplier() * (_KarmanUnlitScale > 0 ? _KarmanUnlitScale : 38197.0); }

#undef _WorldSpaceCameraPos
#define _WorldSpaceCameraPos float3(0, 0, 0)
#endif
