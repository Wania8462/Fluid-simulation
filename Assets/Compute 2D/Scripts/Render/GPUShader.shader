Shader "Custom/TestShaderGPU"
{
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "UnityIndirect.cginc"

            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs

            StructuredBuffer<float2> Positions;
            StructuredBuffer<float4> Colors;

            struct v2f
            {
                float4 pos : SV_POSITION;
                nointerpolation float4 color : COLOR0;
            };

            v2f vert(appdata_base v, uint instanceID : SV_InstanceID)
            {
                v2f o;

                float4 localPos = float4(
                    v.vertex.x + Positions[instanceID].x,
                    v.vertex.y + Positions[instanceID].y,
                    0,
                    1
                );

                o.pos = UnityObjectToClipPos(localPos);
                o.color = Colors[instanceID];

                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }
}