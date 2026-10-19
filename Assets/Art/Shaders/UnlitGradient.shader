// Vertical two-colour gradient, unlit. Used for the lobby's tall windows (night sky over the city)
// and the spirit vision backdrop. Bottom colour at uv.y = 0, top colour at uv.y = 1.
Shader "Deadlock/UnlitGradient"
{
    Properties
    {
        [HDR] _Bottom ("Bottom", Color) = (0.25, 0.45, 0.45, 1)
        [HDR] _Top ("Top", Color) = (0.05, 0.1, 0.16, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            fixed4 _Bottom;
            fixed4 _Top;

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
                return lerp(_Bottom, _Top, saturate(i.uv.y));
            }
            ENDCG
        }
    }
}
