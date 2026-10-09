// PA-102X-S spike: wood + soil mesh. Geometry and per-vertex light come from the SDF bake (vertex texture fetch);
// bark/soil colour and bark relief are the sketch's procedural functions of the sketch-space position.
Shader "PocketArbor/Spike/Wood"
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
            Name "SketchWood"
            ZWrite On ZTest LEqual Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SketchCommon.hlsl"
            #include "SketchBakeFetch.hlsl"

            struct A { float3 pos : POSITION; float3 nrm : NORMAL; float3 texel : TEXCOORD0; };
            struct V { float4 cs : SV_POSITION; float3 ps : TEXCOORD0; float3 n : TEXCOORD1; float4 lt : TEXCOORD2; float2 mt : TEXCOORD3; };

            V vert(A v)
            {
                SkBaked b = skFetchBaked(v.pos, v.nrm, v.texel);
                V o;
                o.cs = TransformObjectToHClip(b.pos);
                o.ps = b.pos;
                o.n = b.nrm;
                o.lt = b.light;
                o.mt = b.mat.xy;
                return o;
            }

            float4 frag(V i) : SV_Target
            {
                float3 p = i.ps, n = normalize(i.n);
                float tr = i.mt.x, h = saturate(i.mt.y);
                float mat = _SkAge * lerp(.25, 1., tr);
                float3 base = lerp(skSoilC(p), skBarkC(p, mat), h);
                if (h > .5 && mat > .02)
                {
                    float e = .35, h0 = skBarkH(p, mat);
                    float3 gr = float3(skBarkH(p + float3(e, 0, 0), mat) - h0, skBarkH(p + float3(0, e, 0), mat) - h0, skBarkH(p + float3(0, 0, e), mat) - h0) / e;
                    n = normalize(n - _SkBump * mat * (gr - n * dot(gr, n)));
                }
                float sh;
                float3 col = skShade(base, n, i.lt, sh);
                if (_SkSpB > .5 && _SkSpB < 1.5)
                {
                    float3 hh = normalize(_SkL.xyz - float3(0., -SK_SE, -SK_CE));
                    col += _SkSI * .1 * h * _SkAge * pow(max(dot(n, hh), 0.), 20.) * sh;
                }
                return skOut(col);
            }
            ENDHLSL
        }
    }
}
