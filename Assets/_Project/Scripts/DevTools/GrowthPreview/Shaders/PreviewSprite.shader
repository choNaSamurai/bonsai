// PA-103S style slice (DevTools only): straight-alpha textured quads — foliage pads (and their soft shadow underlays),
// pot, soil strip, washi background, reference overlay.
//   COLOR     = tint (linear rgb) and alpha multiplier
//   TEXCOORD1 = (brightness − 1, unused) — fresh growth is lighter; every mesh using this shader must provide UV1
Shader "PocketArbor/DevTools/PreviewSprite"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
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

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half brightness : TEXCOORD1;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS);
                o.uv = input.uv0 * _MainTex_ST.xy + _MainTex_ST.zw;
                o.brightness = 1.0 + input.uv1.x;
                o.color = input.color * _Color;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                return half4(tex.rgb * i.color.rgb * i.brightness, tex.a * i.color.a);
            }
            ENDHLSL
        }
    }
}
