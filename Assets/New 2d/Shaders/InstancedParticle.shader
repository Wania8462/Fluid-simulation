Shader "Instanced/ParticleColor"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            StructuredBuffer<float4> _Positions; // xyz position, w scale
            StructuredBuffer<float4> _Colors;    // rgba

            struct appdata
            {
                float3 vertex : POSITION;
                uint instanceID : SV_InstanceID;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float4 posData = _Positions[v.instanceID];
                float3 worldPos = v.vertex * posData.w + posData.xyz;
                o.pos = UnityWorldToClipPos(worldPos);
                o.color = _Colors[v.instanceID];
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                return i.color;
            }
            ENDHLSL
        }
    }
}
