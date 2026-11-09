Shader "Custom/MarchingSquaresShader"
{
    Properties
    {
        _Color ("Color", Color) = (0, 0.4, 1, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "UnityIndirect.cginc"

            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs

            StructuredBuffer<float2> CasePositions;
            StructuredBuffer<float4> CaseDensities;
            float4 _Color;
            float4 cellSize;
            int caseOffset;
            float densityThreshold;

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            // Slides the case mesh's edge midpoints to where the density crosses the threshold.
            // d holds the corner densities (TL, TR, BL, BR)
            float2 InterpolateVertex(float2 v, float4 d)
            {
                bool isTopMid    = abs(v.x) < 0.01 && v.y >  0.4;
                bool isRightMid  = v.x >  0.4 && abs(v.y) < 0.01;
                bool isBottomMid = abs(v.x) < 0.01 && v.y < -0.4;
                bool isLeftMid   = v.x < -0.4 && abs(v.y) < 0.01;

                if (isTopMid)
                {
                    float denom = d.y - d.x;
                    float t = abs(denom) > 0.0001 ? saturate((densityThreshold - d.x) / denom) : 0.5;
                    v.x = lerp(-0.5, 0.5, t);
                }
                else if (isRightMid)
                {
                    float denom = d.w - d.y;
                    float t = abs(denom) > 0.0001 ? saturate((densityThreshold - d.y) / denom) : 0.5;
                    v.y = lerp(0.5, -0.5, t);
                }
                else if (isBottomMid)
                {
                    float denom = d.w - d.z;
                    float t = abs(denom) > 0.0001 ? saturate((densityThreshold - d.z) / denom) : 0.5;
                    v.x = lerp(-0.5, 0.5, t);
                }
                else if (isLeftMid)
                {
                    float denom = d.z - d.x;
                    float t = abs(denom) > 0.0001 ? saturate((densityThreshold - d.x) / denom) : 0.5;
                    v.y = lerp(0.5, -0.5, t);
                }

                return v;
            }

            v2f vert(appdata_base v, uint instanceID : SV_InstanceID)
            {
                v2f o;
                int idx = caseOffset + instanceID;
                float2 center    = CasePositions[idx];
                float4 densities = CaseDensities[idx];

                float2 pos2d = InterpolateVertex(float2(v.vertex.x, v.vertex.y), densities);

                float4 localPos = float4(
                    pos2d.x * cellSize.x + center.x,
                    pos2d.y * cellSize.y + center.y,
                    0, 1
                );
                o.pos = UnityObjectToClipPos(localPos);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }
}
