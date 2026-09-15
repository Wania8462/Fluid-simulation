Shader "Hidden/ScreenSpaceFluid/Composite"
{
    SubShader
    {
        // Drawn over a copy of the scene with Blit. Shades the fluid, or shows one stage of the pipeline for debugging
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "Composite"    // ScreenSpaceFluidRenderer.CompositePassName

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            #include "Assets/3D/RenderCode/ScreenSpace/FluidCommon.hlsl"

            // Values of ScreenSpaceFluidRenderer.DebugView
            static const int ViewRawDepth = 1;
            static const int ViewSmoothDepth = 2;
            static const int ViewNormals = 3;
            static const int ViewThickness = 4;
            static const int ViewSmoothThickness = 5;

            // Share of light that water reflects when seen straight on, the F0 of Schlick's Fresnel approximation
            static const float WaterReflectance = 0.02;

            // A rough match, in linear colour, for Unity's default procedural skybox, which 3DSandbox uses
            static const float3 SkyZenith = float3(0.16, 0.32, 0.65);
            static const float3 SkyHorizon = float3(0.6, 0.68, 0.78);
            static const float3 GroundColor = float3(0.12, 0.11, 0.1);

            // Specular power of the sun highlight (higher is smaller), and its brightness: high enough to show after the 2%
            // that water reflects seen straight on. The HDR camera target keeps values above 1
            static const float SunSharpness = 500;
            static const float SunIntensity = 30;

            // Read by name, like FluidSmooth's sources, not through Blit's _MainTex
            Texture2D _FluidSceneTex;       // Copy of the camera target, the scene behind the fluid
            Texture2D _FluidDepthTex;       // R: smoothed eye depth, G: raw eye depth
            Texture2D _FluidThicknessTex;   // Half resolution. World units of fluid along each view ray
            SamplerState sampler_linear_clamp;

            float3 extinction;
            float refractionStrength;
            float maxRefractionOffset;
            float3 lightDirection;          // Direction the light travels in, a global set by RenderManager
            int debugView;
            float depthDisplayScale;
            float thicknessDisplayScale;

            float4 SampleLinear(Texture2D source, float2 uv)
            {
                return source.SampleLevel(sampler_linear_clamp, uv, 0);
            }

            // value / scale as a grey shade. The camera target is linear, so the value is converted for the shown shade to match
            float4 DebugShade(float value, float scale)
            {
                return float4(GammaToLinearSpace(saturate(value / scale).xxx), 1);
            }

            // Sky and sun seen along a world-space direction. The ground is blended in over a narrow band below the horizon,
            // so its edge doesn't alias
            float3 Environment(float3 direction)
            {
                float3 sky = lerp(SkyHorizon, SkyZenith, sqrt(saturate(direction.y)));
                float sun = pow(saturate(dot(direction, -normalize(lightDirection))), SunSharpness) * SunIntensity;
                return lerp(GroundColor, sky + sun, smoothstep(-0.05, 0, direction.y));
            }

            // View-space position of the smoothed surface at uv
            float3 SurfacePosition(float2 uv)
            {
                return ViewRay(uv) * SamplePoint(_FluidDepthTex, uv).r;
            }

            // Difference to the neighbour one texel along texel, or from the opposite neighbour, whichever is closer in
            // depth. At a silhouette, or where one sheet of fluid overlaps another, that keeps the difference on this
            // pixel's surface. A neighbour with no fluid is FLUID_NO_DEPTH away, so it's only used if both neighbours are
            float3 SurfaceDifference(float3 centre, float2 uv, float2 texel)
            {
                float3 forward = SurfacePosition(uv + texel) - centre;
                float3 backward = centre - SurfacePosition(uv - texel);
                return abs(forward.z) < abs(backward.z) ? forward : backward;
            }

            // View-space normal of the smoothed surface, facing the camera (+z)
            float3 ViewNormal(float2 uv, float eyeDepth)
            {
                float width, height;
                _FluidDepthTex.GetDimensions(width, height);

                float3 centre = ViewRay(uv) * eyeDepth;
                float3 right = SurfaceDifference(centre, uv, float2(1 / width, 0));
                float3 up = SurfaceDifference(centre, uv, float2(0, 1 / height));
                return normalize(cross(right, up));
            }

            float4 frag(v2f_img i) : SV_Target
            {
                float4 scene = SampleLinear(_FluidSceneTex, i.uv);
                float4 depth = SamplePoint(_FluidDepthTex, i.uv);
                float thickness = SampleLinear(_FluidThicknessTex, i.uv).r;

                // Shown wherever thickness was drawn, including where it spills past the depth silhouette
                if (debugView == ViewThickness || debugView == ViewSmoothThickness)
                    return thickness > 0 ? DebugShade(thickness, thicknessDisplayScale) : scene;

                if (depth.g >= FLUID_NO_DEPTH)
                    return scene;

                if (debugView == ViewRawDepth || debugView == ViewSmoothDepth)
                    return DebugShade(debugView == ViewRawDepth ? depth.g : depth.r, depthDisplayScale);

                float3 viewNormal = ViewNormal(i.uv, depth.r);
                float3 normal = mul((float3x3)_FluidCamToWorld, viewNormal);

                // World-space normal as a colour: x red, y green, z blue, each with 0.5 meaning 0
                if (debugView == ViewNormals)
                    return float4(GammaToLinearSpace(normal * 0.5 + 0.5), 1);

                // Schlick's Fresnel: the share of light reflected rises from 2% seen straight on to all of it at grazing
                // angles. 1 + dot(normal, viewDirection) is 1 - the cosine of the angle to the normal
                float3 viewDirection = normalize(mul((float3x3)_FluidCamToWorld, ViewRay(i.uv)));
                float fresnel = WaterReflectance + (1 - WaterReflectance) * pow(saturate(1 + dot(normal, viewDirection)), 5);
                float3 reflected = Environment(reflect(viewDirection, normal));

                // Light entering water bends away from the way the surface tilts, so the scene behind is looked up against
                // the view-space normal, further the more fluid the light passes through. The shift is in world units,
                // turned into uv at the surface's depth
                float2 uvPerUnit = float2(_FluidProj._m00, _FluidProj._m11) / (2 * depth.r);
                float2 offset = clamp(-viewNormal.xy * thickness * refractionStrength * uvPerUnit, -maxRefractionOffset, maxRefractionOffset);

                // Beer-Lambert absorption along the path through the fluid
                float3 refracted = SampleLinear(_FluidSceneTex, i.uv + offset).rgb * exp(-thickness * extinction);

                return float4(lerp(refracted, reflected, fresnel), 1);
            }
            ENDCG
        }
    }
}
