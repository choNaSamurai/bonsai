// PA-102X-S spike: all leaves in one mesh, quad expanded in the vertex shader as the sketch's LVS,
// procedural leaf shape (palm / ovate / needle) and two-sided light with translucency as LFS. July, no wind.
Shader "PocketArbor/Spike/Leaves"
{
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "SketchLeaves"
            ZWrite On ZTest LEqual Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SketchCommon.hlsl"

            struct A { float3 pos : POSITION; float3 nrm : NORMAL; float4 tan : TANGENT; float2 q : TEXCOORD0; float2 sz : TEXCOORD1; float4 col : TEXCOORD2; };
            struct V { float4 cs : SV_POSITION; float2 uv : TEXCOORD0; float3 n : TEXCOORD1; float4 c : TEXCOORD2; };

            V vert(A v)
            {
                float3 d = normalize(v.tan.xyz), n = normalize(v.nrm);
                float3 sd = normalize(cross(n, d));
                float3 P = v.pos + d * (v.q.y * v.sz.x) + sd * (v.q.x * v.sz.y);
                V o;
                o.cs = TransformObjectToHClip(P);
                o.uv = v.q;
                o.n = n;
                o.c = float4(v.col.rgb, min(v.col.a, 1.));
                return o;
            }

            float skPalm(float2 p, out float vn)
            {
                float2 r = float2(p.x, p.y - .36);
                float rl = length(r) / .64, th = atan2(r.x, r.y), R = .24;
                vn = 9.;
                [unroll] for (int i = 0; i < 4; i++)
                {
                    float A = float(i) * .64 + (i == 3 ? .03 : 0.);
                    float Lg = i == 0 ? 1. : (i == 1 ? .94 : (i == 2 ? .74 : .42));
                    [unroll] for (int s = 0; s < 2; s++)
                    {
                        if (i == 0 && s == 1) continue;
                        float dl = abs(th - (s == 0 ? A : -A));
                        R = max(R, Lg * pow(max(0., 1. - dl / .4), .8));
                        vn = min(vn, dl * rl * 5.);
                    }
                }
                R += .022 * sin(th * 44.) * step(.32, R);
                vn = min(vn, abs(p.x) * 40. + step(0., p.y - .36) * 9.);
                return rl - R;
            }

            float skOvate(float2 p, out float vn)
            {
                float y = p.y;
                float w = .46 * pow(max(sin(3.1416 * saturate(y)), 0.), .75) * (1. - .5 * y * y * y) - .028 * abs(frac(y * 15.) - .5);
                vn = min(abs(p.x) * 24., abs(frac((y - abs(p.x) * 1.3) * 6.) - .5) * 5.);
                return abs(p.x) - w;
            }

            float4 frag(V i) : SV_Target
            {
                float2 p = i.uv;
                float vn = 9., d;
                if (_SkSpB < .5) d = skPalm(p, vn);
                else if (_SkSpB < 1.5) d = skOvate(p, vn);
                else { d = abs(p.x) - .5 * (1. - pow(saturate(p.y), 5.)); vn = 9.; }
                if (d > 0.) discard;
                float3 Vv = float3(0., SK_SE, SK_CE), L = _SkL.xyz, n = normalize(i.n);
                bool bk = dot(n, Vv) < 0.;
                if (bk) n = -n;
                float ndl = dot(n, L), dif = max(ndl, 0.), tr = max(-ndl, 0.);
                float vE = i.c.a;
                float3 c = i.c.rgb * (bk ? float3(1.1, 1.15, .85) : float3(1., 1., 1.)) * lerp(.72, 1., smoothstep(0., 1., vn));
                if (_SkSpB > 1.5) c *= .75 + .35 * p.y;
                float3 col = c * (2.7 * _SkSun * dif * vE * _SkSunC.rgb + (.7 + .35 * n.y) * (.35 + .65 * vE) * (1. + .6 * (1. - _SkSun)) * _SkAmb.rgb * float3(.96, 1.02, 1.))
                           + c * float3(1.2, 1.45, .55) * _SkTr * _SkSun * tr * vE * _SkSunC.rgb;
                return skOut(col);
            }
            ENDHLSL
        }
    }
}
