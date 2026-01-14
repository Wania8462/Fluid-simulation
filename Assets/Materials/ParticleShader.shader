Shader "Unlit/Instanced/CubePoints"
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

            StructuredBuffer<float3> Points;
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

                float3 worldPos = v.vertex + Points[instanceID];

                v2f o;
                o.pos = UnityObjectToClipPos(float4(worldPos, 1));
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