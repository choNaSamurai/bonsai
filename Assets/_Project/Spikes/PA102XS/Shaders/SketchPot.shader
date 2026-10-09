// PA-102X-S spike: procedural pot (Surface Nets mesh of potD). Per-pixel normal from the analytic potD gradient,
// per-vertex baked shadow/AO/canopy shadow, the sketch's potC glaze and highlights.
Shader "PocketArbor/Spike/Pot"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "SketchPot"
            ZWrite On ZTest LEqual Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "TreeSdf.hlsl"
            #include "SketchBakeFetch.hlsl"

            struct A { float3 pos : POSITION; float3 nrm : NORMAL; float3 texel : TEXCOORD0; };
            struct V { float4 cs : SV_POSITION; float3 ps : TEXCOORD0; float4 lt : TEXCOORD1; };

            V vert(A v)
            {
                SkBaked b = skFetchBaked(v.pos, v.nrm, v.texel);
                V o;
                o.cs = TransformObjectToHClip(v.pos);
                o.ps = v.pos;
                o.lt = b.light;
                return o;
            }

            float4 frag(V i) : SV_Target
            {
                float3 p = i.ps;
                float3 n = skPotNrm(p);
                float3 base = skPotC(p);
                float sh;
                float3 col = skShade(base, n, i.lt, sh);
                float3 D = float3(0., -SK_SE, -SK_CE);
                float3 hh = normalize(_SkL.xyz - D);
                float gl = _SkPotT > 1.5 ? .55 : (_SkPotT > .5 ? .12 : .05);
                float ex = _SkPotT > 1.5 ? 70. : 22.;
                col += _SkSI * (gl * pow(max(dot(n, hh), 0.), ex) * sh) + length(_SkAmb.rgb) * .58 * (_SkPotT > 1.5 ? .05 * pow(1. - max(dot(n, -D), 0.), 4.) : 0.);
                return skOut(col);
            }
            ENDHLSL
        }
    }
}
