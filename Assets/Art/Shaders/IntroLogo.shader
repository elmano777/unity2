// Glowing title art for the intro (the wheel logo and the DEADLOCK wordmark), drawn from textures made
// by Tools/make_intro_tex.py: R = glyph, G = soft halo, B = reveal order. _Reveal 0..1 burns the glyph
// in following B, with a bright edge on the front; the halo is added on top (premultiplied blend), so
// it brightens the dusk sky instead of darkening it. Stereo macros so it renders in both eyes on Quest.
Shader "Deadlock/IntroLogo"
{
    Properties
    {
        _MainTex ("Glyph (R) Halo (G) Order (B)", 2D) = "black" {}
        [HDR] _Color ("Glyph", Color) = (0.75, 1, 0.92, 1)
        [HDR] _GlowColor ("Halo", Color) = (0.15, 0.95, 0.7, 1)
        [HDR] _EdgeColor ("Burn edge", Color) = (1.6, 2.2, 1.9, 1)
        _Reveal ("Reveal", Range(0, 1.2)) = 1
        _Fade ("Fade", Range(0, 1)) = 1
        _Flash ("Flash", Range(0, 2)) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        Cull Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            half4 _Color;
            half4 _GlowColor;
            half4 _EdgeColor;
            float _Reveal;
            float _Fade;
            float _Flash;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half3 t = tex2D(_MainTex, i.uv).rgb;

                // Shown where the reveal front has passed; a thin band right behind the front glows hot.
                float shown = saturate((_Reveal - t.b) / 0.04);
                float edge = saturate(1 - abs(_Reveal - t.b - 0.03) / 0.05) * step(t.b, _Reveal + 0.02);

                float glyph = t.r * shown;
                float halo = t.g * saturate(_Reveal * 1.5 - t.b * 0.5);

                half3 rgb = glyph * _Color.rgb * (1 + _Flash)
                          + halo * _GlowColor.rgb * (0.9 + _Flash)
                          + t.r * edge * _EdgeColor.rgb;
                return half4(rgb, glyph * 0.9) * _Fade;
            }
            ENDCG
        }
    }
}
