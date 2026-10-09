// PA-102X-S spike, G6: flat colour with depth write/test, to probe depth between sorting orders/layers in the 2D Renderer.
Shader "PocketArbor/Spike/DepthProbe"
{
    Properties { _Color ("Color", Color) = (1, 0, 0, 1) }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZWrite On ZTest LEqual Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            CBUFFER_END
            float4 vert(float3 pos : POSITION) : SV_POSITION { return TransformObjectToHClip(pos); }
            float4 frag() : SV_Target { return _Color; }
            ENDHLSL
        }
    }
}
