// PA-103S style slice (DevTools only): painted bark along C1 ribbon strips (ADR-0004 §1 "Кора, UV").
// Vertex stream (TreePreviewMeshBuilder.BuildTextured):
//   TEXCOORD0 = (u01 across the ribbon: 0 on the +side edge, 1 on the −side edge; arc length along the axis, world units)
//   TEXCOORD1 = (width in world units, barkAge 0 young … 1 trunk, ink factor 0…1, twist phase φ in bark tiles — PA-102R)
//   TEXCOORD2 = unit side vector (world XY) that points towards the u01 = 0 edge
//   TEXCOORD3 = PA-102R AC21 collar: (parent cylinder normal xy, signed distance outside the parent's silhouette in world units — 1e4 = none,
//               light blend: ≥ 0 — the blend itself (0 = parent's normal … 1 = own); < 0 — −blend length: smoothstep(0, length, distance))
//   TEXCOORD4 = (edge mask 0…1 — × ink and the AA fall-off of the nearest edge (roots' inner edge at the join, ADR-0004 rev.5);
//               parent barkAge, blended to the own barkAge with the light blend)
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
        _BarkTwistShear ("Bark twist shear (u per tile of arc)", Float) = 0.3
        _CollarOverlapPx ("Collar overlap inside the parent (px)", Float) = 3.2
        // PA-102T (BALANCE §1.6.6, TD-068): bark of the species — material only, the mesh is the same for every species.
        _BarkTileScale ("Tile scale (species texture density)", Float) = 1
        _BarkAspect ("Texture h/w (1:2 textures = 2)", Float) = 1
        _BarkContrast ("Contrast around the texture mean", Float) = 1
        _BarkSheen ("Sheen along the lit edge", Range(0, 1)) = 0
        _BarkNeutralize ("Placeholder: texture luminance x tint", Range(0, 1)) = 0
        _YoungTipTint ("Young shoot tip tint", Color) = (1, 1, 1, 1)
        _YoungTipWidth ("Young tip: full tint below x, none above y (world width)", Vector) = (0.7, 2.3, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        // PA-102R: separate alpha blend (over operator, outA = a + dstA·(1 − a)) — with the colour factors on alpha, partial coverage over an
        // opaque layer left outA < 1 and the captured PNGs showed light seams / «patches» where ribbons overlap (AC21).
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
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
                float _BarkTwistShear;
                float _CollarOverlapPx;
                float _BarkTileScale;
                float _BarkAspect;
                half _BarkContrast;
                half _BarkSheen;
                half _BarkNeutralize;
                half4 _YoungTipTint;
                float4 _YoungTipWidth;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
                float4 uv3 : TEXCOORD3;
                float2 uv4 : TEXCOORD4;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 barkUV : TEXCOORD0;   // tiled bark coordinates
                float s : TEXCOORD1;         // signed across: +1 at the side-vector edge, −1 at the other (beyond ±1 = AA fringe)
                float2 side : TEXCOORD2;     // side vector (world)
                half3 ageInk : TEXCOORD3;    // barkAge, ink factor, young-tip share (by width)
                float4 collar : TEXCOORD4;   // parent normal xy, distance outside the parent, light blend
                float2 mask : TEXCOORD5;     // edge mask
                half4 color : COLOR;
            };

            // PA-102T: species bark. Placeholder (neutralise 1): luminance of the shared texture / its mean × tint; species texture: × tint (white).
            // Contrast scales the deviation from the mean colour (mean = the last mip).
            half3 SpeciesBark(half3 tex, half3 mean, half3 tint)
            {
                const half3 w = half3(0.2126, 0.7152, 0.0722);
                half meanLum = max(dot(mean, w), 1e-3);
                half3 placeholder = tint * (dot(tex, w) / meanLum);
                half3 c = lerp(tex * tint, placeholder, _BarkNeutralize);
                half3 m = lerp(mean * tint, tint, _BarkNeutralize);
                return max(m + (c - m) * _BarkContrast, 0.0);
            }

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
                float scale = max(_BarkTileScale, 1e-3);
                o.barkUV = float2(((0.5 - input.uv0.x) * width / tile * max(_BarkAspect, 1e-3) + _BarkTwistShear * input.uv1.w) * scale, input.uv0.y / tile * scale);
                o.side = input.uv2;
                o.ageInk = half3(input.uv1.y, input.uv1.z, 1.0 - smoothstep(_YoungTipWidth.x, _YoungTipWidth.y, input.uv1.x));
                o.color = input.color;
                o.collar = input.uv3;
                o.mask = input.uv4;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                // AC21: collar blend from the parent's light / bark age to the own over the blend length outside the parent's edge.
                float blend = i.collar.w >= 0.0 ? saturate(i.collar.w) : smoothstep(0.0, max(-i.collar.w, 1e-4), i.collar.z);
                half age = lerp(i.mask.y, i.ageInk.x, blend);
                half3 youngTint = lerp(_YoungTint.rgb, _YoungTipTint.rgb, i.ageInk.z);
                half3 trunk = SpeciesBark(SAMPLE_TEXTURE2D(_BarkTrunk, sampler_BarkTrunk, i.barkUV).rgb,
                    SAMPLE_TEXTURE2D_LOD(_BarkTrunk, sampler_BarkTrunk, float2(0.5, 0.5), 12).rgb, _TrunkTint.rgb);
                half3 young = SpeciesBark(SAMPLE_TEXTURE2D(_BarkYoung, sampler_BarkYoung, i.barkUV).rgb,
                    SAMPLE_TEXTURE2D_LOD(_BarkYoung, sampler_BarkYoung, float2(0.5, 0.5), 12).rgb, youngTint);
                half3 bark = lerp(young, trunk, saturate(age)) * i.color.rgb;

                // Fake cylinder: the across coordinate becomes a normal bulging towards the viewer.
                float s = clamp(i.s, -1.0, 1.0);
                float3 n = float3(normalize(i.side + 1e-6) * s, sqrt(saturate(1.0 - s * s)));
                // AC21: at the collar the light comes from the parent's cylinder normal (no brightness step at the junction).
                float3 np = float3(i.collar.xy, sqrt(saturate(1.0 - dot(i.collar.xy, i.collar.xy))));
                n = normalize(lerp(np, n, blend) + float3(0.0, 0.0, 1e-4));
                float3 l = normalize(_LightDir.xyz);
                half lambert = saturate(dot(n, l));
                bark *= lerp(_ShadeDark, _ShadeLight, smoothstep(0.05, 0.95, lambert));
                // PA-102T: sheen — a soft highlight band along the lit edge (Yamazakura 0.30, Momiji 0.10).
                float3 h = normalize(l + float3(0.0, 0.0, 1.0));
                bark += _BarkSheen * pow(saturate(dot(n, h)), 24.0) * half3(0.85, 0.8, 0.75);

                // Pixel distance to the geometric edge (|s| = 1) → coverage AA and ink outline.
                float ds = max(fwidth(i.s), 1e-5);
                float edgePx = (1.0 - abs(i.s)) / ds;
                float widthPx = 2.0 / ds;
                half edgeMask = saturate(i.mask.x);
                half coverage = lerp(1.0, saturate(edgePx + 0.5), edgeMask);
                float inkW = min(_InkPx, _InkMaxShare * widthPx);
                // AC21: a lateral is transparent inside its parent except an overlap band that covers the parent's ink line; its own ink
                // starts only outside the parent (no straight edges of the ribbon start inside the parent).
                float pxWorld = unity_OrthoParams.w > 0.5 ? max(2.0 * unity_OrthoParams.y / max(_ScreenParams.y, 1.0), 1e-5) : 0.1;
                float d = i.collar.z;
                half collarAlpha = saturate((d + _CollarOverlapPx * pxWorld) / pxWorld + 0.5);
                half collarInk = saturate(d / max(_InkPx * pxWorld, 1e-5));
                half ink = (1.0 - smoothstep(inkW - 0.6, inkW + 0.6, edgePx)) * _InkColor.a * i.ageInk.y * edgeMask * collarInk;
                bark = lerp(bark, _InkColor.rgb, ink);

                return half4(bark, coverage * i.color.a * collarAlpha);
            }
            ENDHLSL
        }
    }
}
