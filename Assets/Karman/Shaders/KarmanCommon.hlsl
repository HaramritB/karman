#ifndef KARMAN_COMMON_INCLUDED
#define KARMAN_COMMON_INCLUDED

#define K_PI 3.14159265359

// Equirectangular UV from a direction in a body's object space (local +Y = north pole, +X = prime meridian,
// +Z = 90°E). Uses Tarini's dual-seam trick so mip selection does not break at the antimeridian.
float2 KarmanEquirectUV(float3 d)
{
    float lon = atan2(d.z, d.x);
    float lat = asin(clamp(d.y, -1.0, 1.0));
    float uA = lon / (2.0 * K_PI) + 0.5;
    float uB = frac(uA + 0.5) - 0.5;
    float u = (fwidth(uA) <= fwidth(uB) + 1e-6) ? uA : uB;
    return float2(u, lat / K_PI + 0.5);
}

// Ray/sphere: returns (tNear, tFar); tFar < 0 or tNear > tFar means miss.
float2 KarmanRaySphere(float3 ro, float3 rd, float3 c, float r)
{
    float3 oc = ro - c;
    float b = dot(oc, rd);
    float cc = dot(oc, oc) - r * r;
    float h = b * b - cc;
    if (h < 0.0) return float2(1e30, -1e30);
    h = sqrt(h);
    return float2(-b - h, -b + h);
}

// Kasten & Young (1989) relative air mass, clamped below the horizon.
float KarmanAirMass(float cosZenith)
{
    float z = degrees(acos(clamp(cosZenith, -1.0, 1.0)));
    z = min(z, 93.0);
    return 1.0 / (max(cos(radians(z)), 0.0) + 0.50572 * pow(max(96.07995 - z, 0.1), -1.6364));
}

float3 KarmanSunTransmittance(float cosZenith, float3 zenithOpticalDepth)
{
    return exp(-zenithOpticalDepth * KarmanAirMass(cosZenith));
}

float KarmanGGX(float NdotH, float roughness)
{
    float a2 = roughness * roughness * roughness * roughness;
    float d = NdotH * NdotH * (a2 - 1.0) + 1.0;
    return a2 / (K_PI * d * d);
}

#endif
