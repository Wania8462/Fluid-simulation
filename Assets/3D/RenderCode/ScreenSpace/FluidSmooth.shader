Shader "Hidden/ScreenSpaceFluid/Smooth"
{
    SubShader
    {
        // Separable blurs drawn with Blit, a horizontal pass then a vertical one. The radius is a world-space length turned
        // into pixels at each pixel's depth, so the smoothing covers the same part of the fluid at any distance or resolution
        Cull Off
        ZWrite Off
        ZTest Always

        CGINCLUDE
        #include "Assets/3D/RenderCode/ScreenSpace/FluidCommon.hlsl"

        // Most taps on each side of a pixel. The largest maxScreenRadius, 0.03, just reaches it on a 2160-pixel-tall (4K) target
        #define MAX_RADIUS 64

        // ScreenSpaceFluidRenderer's temporary render textures, which it ping-pongs between. Each pass reads its source by
        // name: in Play mode, Blit's _MainTex gave these passes scene colours instead of the texture passed to Blit
        Texture2D _FluidDepthTex;       // R: smoothed eye depth, G: raw eye depth
        Texture2D _FluidDepthTemp;
        Texture2D _FluidThicknessTex;   // World units of fluid along each view ray
        Texture2D _FluidThicknessTemp;

        float smoothingRadius;      // World units, 3 standard deviations
        float maxScreenRadius;      // Fraction of the target's height
        float depthRangeSigma;      // World units

        // Radius in pixels at an eye depth, in a texture height pixels tall, and the Gaussian falloff that ends close to
        // zero at that radius
        int BlurRadius(float eyeDepth, float height, out float falloff)
        {
            float radius = min(smoothingRadius * PixelsPerUnit(eyeDepth, height), maxScreenRadius * height);

            // Standard deviation radius / 3, so exp(x * x * falloff) = exp(-x * x / (2 * sigma * sigma))
            falloff = -4.5 / max(radius * radius, 1e-4);
            return min((int)ceil(radius), MAX_RADIUS);
        }

        // Smooths R, the current depth. Each tap is weighted by its distance and by how close its raw depth (G) is to this
        // pixel's, so separate sheets of fluid and body edges don't blend together. G passes through unchanged
        float4 Bilateral(Texture2D source, float2 uv, float2 direction)
        {
            float width, height;
            source.GetDimensions(width, height);

            float4 centre = SamplePoint(source, uv);
            if (centre.g >= FLUID_NO_DEPTH)
                return centre;

            float falloff;
            int radius = BlurRadius(centre.g, height, falloff);
            float rangeFalloff = -0.5 / max(depthRangeSigma * depthRangeSigma, 1e-6);
            float2 texelStep = direction / float2(width, height);

            // A tap with no fluid differs in depth by about FLUID_NO_DEPTH, so its weight is zero
            float sum = 0;
            float weightSum = 0;
            [loop] for (int x = -radius; x <= radius; x++)
            {
                float2 tap = SamplePoint(source, uv + texelStep * x).rg;
                float depthDifference = tap.g - centre.g;
                float weight = exp(x * x * falloff + depthDifference * depthDifference * rangeFalloff);
                sum += tap.r * weight;
                weightSum += weight;
            }

            return float4(sum / weightSum, centre.g, 0, 0);
        }

        // Plain Gaussian on R, with its radius from the eye depth at the same place. Pixels with no depth are left as they
        // are, because the composite only reads thickness where there is depth
        float4 Gauss(Texture2D source, float2 uv, float2 direction)
        {
            float width, height;
            source.GetDimensions(width, height);

            float4 centre = SamplePoint(source, uv);
            float eyeDepth = SamplePoint(_FluidDepthTex, uv).g;
            if (eyeDepth >= FLUID_NO_DEPTH)
                return centre;

            float falloff;
            int radius = BlurRadius(eyeDepth, height, falloff);
            float2 texelStep = direction / float2(width, height);

            float sum = 0;
            float weightSum = 0;
            [loop] for (int x = -radius; x <= radius; x++)
            {
                float weight = exp(x * x * falloff);
                sum += SamplePoint(source, uv + texelStep * x).r * weight;
                weightSum += weight;
            }

            return float4(sum / weightSum, 0, 0, 0);
        }
        ENDCG

        Pass
        {
            Name "BilateralH"    // ScreenSpaceFluidRenderer.BilateralHPassName

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            float4 frag(v2f_img i) : SV_Target
            {
                return Bilateral(_FluidDepthTex, i.uv, float2(1, 0));
            }
            ENDCG
        }

        Pass
        {
            Name "BilateralV"    // ScreenSpaceFluidRenderer.BilateralVPassName

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            float4 frag(v2f_img i) : SV_Target
            {
                return Bilateral(_FluidDepthTemp, i.uv, float2(0, 1));
            }
            ENDCG
        }

        Pass
        {
            Name "GaussH"    // ScreenSpaceFluidRenderer.GaussHPassName

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            float4 frag(v2f_img i) : SV_Target
            {
                return Gauss(_FluidThicknessTex, i.uv, float2(1, 0));
            }
            ENDCG
        }

        Pass
        {
            Name "GaussV"    // ScreenSpaceFluidRenderer.GaussVPassName

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            float4 frag(v2f_img i) : SV_Target
            {
                return Gauss(_FluidThicknessTemp, i.uv, float2(0, 1));
            }
            ENDCG
        }
    }
}
