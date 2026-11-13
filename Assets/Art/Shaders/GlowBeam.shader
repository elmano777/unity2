// Additive beam for LineRenderers: soft across the width only (Deadlock/GlowParticle fades
// radially, which would also fade a line towards both ends), with bright pulses running from the
// start of the line to its end so the energy reads as travelling towards the target.
// Vertex colour multiplies everything; stereo macros so it renders in both eyes on Quest.
Shader "Deadlock/GlowBeam"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, 1, 1, 1)
        _PulseScale ("Pulses along the beam", Float) = 6
        _PulseSpeed ("Pulse speed", Float) = 9
        _PulseAmount ("Pulse strength (0 = steady)", Range(0, 1)) = 0.45
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
            float _PulseScale, _PulseSpeed, _PulseAmount;

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
                float across = 1 - saturate(abs(i.uv.y - 0.5) * 2);
                float pulse = 1 - _PulseAmount + _PulseAmount * (0.5 + 0.5 * sin((i.uv.x * _PulseScale - _Time.y * _PulseSpeed) * 6.2832));
                fixed4 c = _Color * i.color;
                return fixed4(c.rgb, saturate(c.a * across * across * pulse));
            }
            ENDCG
        }
    }
}
