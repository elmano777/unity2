// Unlit additive glow for the lobby and the spirit vision: floor runes, crystals, candle flames,
// the seer's eyes. _Radial > 0 fades the quad/disc from its centre outwards (soft light pools).
// Stereo macros so it renders in both eyes on Quest.
Shader "Deadlock/Glow"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.3, 1, 0.55, 1)
        _Radial ("Radial falloff (0 = flat)", Range(0, 1)) = 0
        _Pulse ("Pulse speed (0 = steady)", Float) = 0
        _Intensity ("Intensity (hover)", Range(0, 3)) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        Cull Off
        Blend SrcAlpha One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Radial;
            float _Pulse;
            float _Intensity;

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
                float radial = 1 - saturate(distance(i.uv, float2(0.5, 0.5)) * 2);
                float shape = lerp(1, radial * radial, _Radial);
                float pulse = _Pulse > 0 ? 0.75 + 0.25 * sin(_Time.y * _Pulse) : 1;
                return fixed4(_Color.rgb, saturate(_Color.a * shape * pulse * _Intensity));
            }
            ENDCG
        }
    }
}
