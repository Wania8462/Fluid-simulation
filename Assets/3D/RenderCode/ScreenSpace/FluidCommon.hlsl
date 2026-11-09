#ifndef FLUID_COMMON_INCLUDED
#define FLUID_COMMON_INCLUDED

#include "UnityCG.cginc"

// Fluid textures store eye depth (-viewPosition.z). Test against this with >=,
// since the clear colour may be gamma-converted upwards
#define FLUID_NO_DEPTH 1e7

float4x4 _FluidProj;
float4x4 _FluidInvProj;
float4x4 _FluidCamToWorld;

// Point sampling, so depth isn't blended with FLUID_NO_DEPTH across silhouettes
SamplerState sampler_point_clamp;

float4 SamplePoint(Texture2D source, float2 uv)
{
    return source.SampleLevel(sampler_point_clamp, uv, 0);
}

// Inverse of LinearEyeDepth
float EyeDepthToRaw(float eyeDepth)
{
    return (1 / eyeDepth - _ZBufferParams.w) / _ZBufferParams.z;
}

// View-space ray scaled so -z = 1; multiply by eye depth for the view-space position
float3 ViewRay(float2 uv)
{
    float3 ray = mul(_FluidInvProj, float4(uv * 2 - 1, 0, 1)).xyz;
    return ray / -ray.z;
}

float PixelsPerUnit(float eyeDepth, float textureHeight)
{
    return _FluidProj._m11 * textureHeight / (2 * eyeDepth);
}

#endif
