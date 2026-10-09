// PA-102X-S spike: B1 per-vertex bake with the sketch's SDF code (TreeSdf.hlsl).
// Input: _SkBakePos (xyz, tmax), _SkBakeNrm (xyz, kind: 0 wood, 1 soil, 2 pot, -1 empty), one texel per vertex.
Shader "Hidden/PocketArbor/Spike/SdfBake"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        HLSLINCLUDE
        #pragma target 3.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "TreeSdf.hlsl"
        #include "SketchBakeFetch.hlsl"

        TEXTURE2D(_SkBakePos);
        TEXTURE2D(_SkBakeNrm);

        struct FsOut { float4 pos : SV_POSITION; };

        FsOut vertFs(uint id : SV_VertexID)
        {
            FsOut o;
            float2 uv = float2((id << 1) & 2, id & 2);
            o.pos = float4(uv * 2.0 - 1.0, 0.5, 1.0);
            return o;
        }

        // Nearest point of the zero set (Newton along the SDF gradient) — for vertices buried inside another part.
        float3 skProject(float3 p)
        {
            const float e = .15;
            [loop] for (int i = 0; i < 8; i++)
            {
                float f = skMapD(p, false);
                if (abs(f) < .01) break;
                float3 g = 0;
                [loop] for (int k = 0; k < 4; k++)
                {
                    float3 o = k == 0 ? float3(1, -1, -1) : k == 1 ? float3(-1, -1, 1) : k == 2 ? float3(-1, 1, -1) : float3(1, 1, 1);
                    g += o * skMapD(p + o * e, false);
                }
                g /= 4. * e;
                p -= g * f / max(dot(g, g), .05);
            }
            return p;
        }

        // Surface point used for light/material (after pass 0). Buried vertices (|S.xyz| = 2) keep their position
        // but take light, material and normal from the nearest surface point, so triangles reaching into the
        // ground/trunk do not smear dark values over the visible surface.
        void skSnapped(int2 c, out float3 q, out float3 n, out float kind)
        {
            float4 P = LOAD_TEXTURE2D(_SkBakePos, c);
            float4 N = LOAD_TEXTURE2D(_SkBakeNrm, c);
            float4 S = LOAD_TEXTURE2D(_SkBakeSnap, c);
            kind = N.w;
            float3 n0 = normalize(N.xyz);
            float s2 = dot(S.xyz, S.xyz);
            bool ok = s2 > 0.25;
            q = ok ? P.xyz + n0 * S.w : P.xyz;
            n = ok ? normalize(S.xyz) : n0;
            if (s2 > 2.25) q = skProject(P.xyz);
        }
        ENDHLSL

        Pass // 0: snap onto the zero set of map() along the mesh normal -> (n, t)
        {
            Name "Snap"
            HLSLPROGRAM
            #pragma vertex vertFs
            #pragma fragment frag
            float4 frag(FsOut i) : SV_Target
            {
                int2 c = int2(i.pos.xy);
                float4 P = LOAD_TEXTURE2D(_SkBakePos, c);
                float4 N = LOAD_TEXTURE2D(_SkBakeNrm, c);
                if (N.w < -0.5) return 0;
                float3 p = P.xyz, n = normalize(N.xyz);
                float tmax = P.w;
                if (N.w > 1.5 || tmax <= 0.) return float4(n, 0.);
                // Soil vertices inside / at the pot wall stay put: otherwise they climb the slanted cavity wall ("teeth").
                if (N.w > 0.5 && skPShape(p, skPotB(p) - 8.) > -1.) return float4(n, 0.);
                float t = 0.;
                float f = skMapD(p, false);
                if (f > 0.)
                {
                    [loop] for (int k = 0; k < 6; k++)
                    {
                        t = max(t - f, -tmax);
                        f = skMapD(p + n * t, false);
                        if (abs(f) < .004) break;
                    }
                }
                else
                {
                    bool found = false;
                    [loop] for (int k = 0; k < 48; k++)
                    {
                        if (f > -.004) { found = true; break; }
                        t += max(-f, .03);
                        if (t > tmax) break;
                        f = skMapD(p + n * t, false);
                    }
                    if (!found) return float4(skNrm(skProject(p)) * 2., 0.); // buried: geometry stays, |n| = 2 flags it
                    [loop] for (int k2 = 0; k2 < 2; k2++) { t -= f; f = skMapD(p + n * t, false); }
                }
                return float4(skNrm(p + n * t), t);
            }
            ENDHLSL
        }

        Pass // 1: light terms (shd, folT, ao, folIn) at the snapped point, as FS does per pixel
        {
            Name "Light"
            HLSLPROGRAM
            #pragma vertex vertFs
            #pragma fragment frag
            float4 frag(FsOut i) : SV_Target
            {
                int2 c = int2(i.pos.xy);
                float3 q, n; float kind;
                skSnapped(c, q, n, kind);
                if (kind < -0.5) return float4(1, 1, 1, 0);
                float3 L = _SkL.xyz;
                float sh = skShd(q + n * .9 + L * .4, L);
                float ft = skFolT(q + n * .5, L);
                float a = skAo(q, n);
                float fi = skFolIn(q);
                return float4(sh, ft, a, fi);
            }
            ENDHLSL
        }

        Pass // 2: material terms (trunk maturity, wood/soil blend, pot flag)
        {
            Name "Mat"
            HLSLPROGRAM
            #pragma vertex vertFs
            #pragma fragment frag
            float4 frag(FsOut i) : SV_Target
            {
                int2 c = int2(i.pos.xy);
                float3 q, n; float kind;
                skSnapped(c, q, n, kind);
                if (kind < -0.5) return 0;
                float3 m = skMap(q, false);
                float tr = max(1. - smoothstep(.5, 3., skTrunkD(q)), 1. - smoothstep(0., .9 * _SkRb, q.y));
                float h = kind > 1.5 ? 0. : m.z;
                return float4(tr, h, kind > 1.5 ? 1. : m.y, 0.);
            }
            ENDHLSL
        }
    }
}
