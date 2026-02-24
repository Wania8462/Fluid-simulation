Shader "Custom/2DParticleShader"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            StructuredBuffer<float2> Points;
            float4 _Color;

            struct appdata
            {
                float3 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vert (appdata v, uint instanceID : SV_InstanceID)
            {
                UNITY_SETUP_INSTANCE_ID(v);

                float2 worldPos = v.vertex.xy + Points[instanceID];

                v2f o;
                o.pos = float4(worldPos, 0, 1);
                return o;
            }

            float4 frag () : SV_Target
            {
                return _Color;
            }
            ENDHLSL
        }
    }
}