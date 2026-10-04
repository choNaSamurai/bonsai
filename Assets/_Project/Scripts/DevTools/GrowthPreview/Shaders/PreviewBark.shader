// PA-103S style slice (DevTools only): painted bark along C1 ribbon strips (ADR-0004 §1 "Кора, UV").
// Vertex stream (TreePreviewMeshBuilder.BuildTextured):
//   TEXCOORD0 = (u01 across the ribbon: 0 on the +side edge, 1 on the −side edge; arc length along the axis, world units)
//   TEXCOORD1 = (width in world units, barkAge 0 young … 1 trunk, ink factor 0…1, unused)
//   TEXCOORD2 = unit side vector (world XY) that points towards the u01 = 0 edge
//   COLOR     = tint (linear)
// Look: bark_trunk ↔ bark_young blend by barkAge, isotropic texel density (u scaled by width), fake cylinder light from the
// upper left, thin dark ink outline along the ribbon edges, analytic edge AA (fwidth) instead of MSAA.
Shader "PocketArbor/DevTools/PreviewBark"
{
    Properties
    {
        _BarkTrunk ("Bark trunk", 2D) = "white" {}
        _BarkYoung ("Bark young", 2D) = "white" {}
        _TrunkTint ("Trunk tint", Color) = (1, 1, 1, 1)
        _YoungTint ("Young tint", Color) = (1, 1, 1, 1)
        _BarkTileWorld ("Bark tile size (world units)", Float) = 16
        _LightDir ("Light direction (towards light, xyz)", Vector) = (-0.55, 0.6, 0.6, 0)
        _ShadeDark ("Shade on the dark flank", Range(0, 1)) = 0.55
        _ShadeLight ("Shade on the lit flank", Range(0.5, 1.5)) = 1.08
        _InkColor ("Ink colour", Color) = (0.13, 0.085, 0.06, 0.9)
        _InkPx ("Ink width (px)", Float) = 1.6
        _InkMaxShare ("Max ink share of the ribbon width", Range(0, 0.5)) = 0.3
        _AAExpandPx ("Edge AA expansion (px)", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BarkTrunk); SAMPLER(sampler_BarkTrunk);
            TEXTURE2D(_BarkYoung); SAMPLER(sampler_BarkYoung);

            CBUFFER_START(UnityPerMaterial)
                float4 _BarkTrunk_ST;
                float4 _BarkYoung_ST;
                half4 _TrunkTint;
                half4 _YoungTint;
                float _BarkTileWorld;
                float4 _LightDir;
                half _ShadeDark;
                half _ShadeLight;
                half4 _InkColor;
                float _InkPx;
                float _InkMaxShare;
                float _AAExpandPx;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 barkUV : TEXCOORD0;   // tiled bark coordinates
                float s : TEXCOORD1;         // signed across: +1 at the side-vector edge, −1 at the other (beyond ±1 = AA fringe)
                float2 side : TEXCOORD2;     // side vector (world)
                half2 ageInk : TEXCOORD3;    // barkAge, ink factor
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float width = max(input.uv1.x, 1e-3);
                float sgn = input.uv0.x < 0.5 ? 1.0 : -1.0;
                // World size of one pixel for the orthographic preview camera (0 for perspective → no expansion).
                float pxWorld = unity_OrthoParams.w > 0.5 ? (2.0 * unity_OrthoParams.y / max(_ScreenParams.y, 1.0)) : 0.0;
                float expand = _AAExpandPx * pxWorld;
                float3 pos = input.positionOS;
                pos.xy += input.uv2 * (sgn * expand);
                o.positionCS = TransformObjectToHClip(pos);
                o.s = sgn * (1.0 + expand / (0.5 * width));
                float tile = max(_BarkTileWorld, 1e-3);
                o.barkUV = float2((0.5 - input.uv0.x) * width / tile, input.uv0.y / tile);
                o.side = input.uv2;
                o.ageInk = half2(input.uv1.y, input.uv1.z);
                o.color = input.color;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half3 trunk = SAMPLE_TEXTURE2D(_BarkTrunk, sampler_BarkTrunk, i.barkUV).rgb * _TrunkTint.rgb;
                half3 young = SAMPLE_TEXTURE2D(_BarkYoung, sampler_BarkYoung, i.barkUV).rgb * _YoungTint.rgb;
                half3 bark = lerp(young, trunk, saturate(i.ageInk.x)) * i.color.rgb;

                // Fake cylinder: the across coordinate becomes a normal bulging towards the viewer.
                float s = clamp(i.s, -1.0, 1.0);
                float3 n = float3(normalize(i.side + 1e-6) * s, sqrt(saturate(1.0 - s * s)));
                float3 l = normalize(_LightDir.xyz);
                half lambert = saturate(dot(n, l));
                bark *= lerp(_ShadeDark, _ShadeLight, smoothstep(0.05, 0.95, lambert));

                // Pixel distance to the geometric edge (|s| = 1) → coverage AA and ink outline.
                float ds = max(fwidth(i.s), 1e-5);
                float edgePx = (1.0 - abs(i.s)) / ds;
                float widthPx = 2.0 / ds;
                half coverage = saturate(edgePx + 0.5);
                float inkW = min(_InkPx, _InkMaxShare * widthPx);
                half ink = (1.0 - smoothstep(inkW - 0.6, inkW + 0.6, edgePx)) * _InkColor.a * i.ageInk.y;
                bark = lerp(bark, _InkColor.rgb, ink);

                return half4(bark, coverage * i.color.a);
            }
            ENDHLSL
        }
    }
}
