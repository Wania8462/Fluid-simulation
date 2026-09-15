// Shared by the screen-space fluid shaders. The fluid textures store eye depth: the positive distance along the
// camera's forward axis, -viewPosition.z

#ifndef FLUID_COMMON_INCLUDED
#define FLUID_COMMON_INCLUDED

#include "UnityCG.cginc"

// Eye depth written where there is no fluid, matching ScreenSpaceFluidRenderer.NoDepth. Test with >=, not ==:
// ClearRenderTarget may convert the clear colour from gamma to linear, which only makes it larger
#define FLUID_NO_DEPTH 1e7

// Camera matrices, set by ScreenSpaceFluidRenderer right before the camera renders. Blit passes read these instead of
// UNITY_MATRIX_P and unity_Camera*, because Blit replaces the projection with its own orthographic one
float4x4 _FluidProj;        // cam.projectionMatrix, OpenGL convention
float4x4 _FluidInvProj;     // Inverse of _FluidProj
float4x4 _FluidCamToWorld;  // cam.cameraToWorldMatrix. View space looks down -z

// Point sampling, because interpolating eye depth across a silhouette would blend in FLUID_NO_DEPTH
SamplerState sampler_point_clamp;

float4 SamplePoint(Texture2D source, float2 uv)
{
    return source.SampleLevel(sampler_point_clamp, uv, 0);
}

// Hardware depth-buffer value for an eye depth, the inverse of LinearEyeDepth. _ZBufferParams already accounts for
// reversed Z
float EyeDepthToRaw(float eyeDepth)
{
    return (1 / eyeDepth - _ZBufferParams.w) / _ZBufferParams.z;
}

// View-space direction through a screen uv, scaled so that -z = 1. Times an eye depth, it's the view-space position.
// uv (0, 0) is the bottom left of the view, like (-1, -1) in the OpenGL convention of _FluidProj
float3 ViewRay(float2 uv)
{
    // Some point on the ray, in homogeneous coordinates. Dividing by -z removes the unknown w scale as well
    float3 ray = mul(_FluidInvProj, float4(uv * 2 - 1, 0, 1)).xyz;
    return ray / -ray.z;
}

// Pixels that one world unit covers at an eye depth, in a texture textureHeight pixels tall. Uses the vertical field of
// view, so the aspect ratio doesn't matter
float PixelsPerUnit(float eyeDepth, float textureHeight)
{
    return _FluidProj._m11 * textureHeight / (2 * eyeDepth);
}

#endif
