// Radial black vignette for ComfortVignette.cs: clear in the middle, dark at the edge, scaled by
// _Strength. Radii are in UV units of a 4 m quad 1 m from the eye (0.19 = ~35 degrees off centre).
// Drawn on top of everything; stereo macros so it renders in both eyes on Quest.
Shader "Deadlock/ComfortVignette"
{
    Properties
    {
        _Color ("Color", Color) = (0, 0, 0, 1)
        _Strength ("Strength", Range(0, 1)) = 0
        _Inner ("Clear radius", Range(0, 0.7)) = 0.07
        _Outer ("Dark radius", Range(0, 0.7)) = 0.19
    }

    SubShader
    {
        Tags { "Queue" = "Overlay+100" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZTest Always
        ZWrite Off
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Strength;
            float _Inner;
            float _Outer;

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
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float d = distance(i.uv, float2(0.5, 0.5));
                // Shrink the clear area as strength rises, so the effect closes in rather than just darkens.
                float inner = lerp(_Outer, _Inner, _Strength);
                float a = smoothstep(inner, _Outer, d) * saturate(_Strength * 1.6);
                return fixed4(_Color.rgb, a * _Color.a);
            }
            ENDCG
        }
    }
}
