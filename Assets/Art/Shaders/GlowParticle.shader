// Additive glow for combat particles and tracers: same look as Deadlock/Glow, but multiplied by
// the vertex colour so a particle system's colour-over-lifetime can fade each spark out.
// Stereo macros so it renders in both eyes on Quest.
Shader "Deadlock/GlowParticle"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, 0.8, 0.4, 1)
        _Radial ("Radial falloff (0 = flat)", Range(0, 1)) = 1
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

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
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
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float radial = 1 - saturate(distance(i.uv, float2(0.5, 0.5)) * 2);
                float shape = lerp(1, radial * radial, _Radial);
                fixed4 c = _Color * i.color;
                return fixed4(c.rgb, saturate(c.a * shape));
            }
            ENDCG
        }
    }
}
