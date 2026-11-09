Shader "Custom/Body3D"
{
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }

        CGINCLUDE
        #include "UnityCG.cginc"
        #include "UnityIndirect.cginc"
        #include "Assets/3D/SimulationCode/RigidBodyMath3D.hlsl"

        #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs

        StructuredBuffer<RigidBodyState> BodyStates;
        StructuredBuffer<RigidBodyProperties> BodyProperties;
        uint drawShape;

        struct v2f
        {
            float4 pos : SV_POSITION;
            float3 normal : TEXCOORD0;
        };

        v2f vert(appdata_base v, uint instanceID : SV_InstanceID)
        {
            RigidBodyProperties props = BodyProperties[instanceID];
            RigidBodyState state = BodyStates[instanceID];

            float visible = props.shape == drawShape;
            float isCube = props.shape == ShapeCube;
            // Bodies of the other shape get zero size, so they collapse to a point and aren't rasterised
            float3 halfExtents = lerp(props.size.xxx, props.size.xyz, isCube) * visible;

            // Meshes are unit sized, so doubling a vertex spans the half extents
            float3 local = v.vertex.xyz * 2 * halfExtents - props.centerOfMass.xyz;
            float3 world = state.position.xyz + QuaternionRotate(state.rotation, local);

            v2f o;
            o.pos = mul(UNITY_MATRIX_VP, float4(world, 1));
            o.normal = QuaternionRotate(state.rotation, v.normal);
            return o;
        }
        ENDCG

        Pass
        {
            ZWrite On

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            static const float ambient = 0.3;

            float4 color;
            float3 lightDirection;

            float4 frag(v2f i) : SV_Target
            {
                float diffuse = saturate(dot(normalize(i.normal), -normalize(lightDirection)));
                return color * (ambient + (1 - ambient) * diffuse);
            }
            ENDCG
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ColorMask 0

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragDepth

            float4 fragDepth(v2f i) : SV_Target
            {
                return 0;
            }
            ENDCG
        }
    }
}
