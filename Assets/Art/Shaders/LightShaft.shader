// Fake volumetric light: additive ribbons/frustums for moonlight through windows and searchlight
// beams. uv.y runs 0 at the source to 1 at the far end (fades out), uv.x 0..1 across (soft edges).
// Faces seen edge-on and fragments close to the eye fade out so the planes never read as cards.
// Stereo macros so it renders in both eyes on Quest.
Shader "Deadlock/LightShaft"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.55, 0.7, 1, 0.08)
        _EdgeSoftness ("Edge softness", Range(0.01, 0.5)) = 0.3
        _FarFade ("Far fade power", Range(0.2, 4)) = 1.4
        _NearFade ("Near camera fade (m)", Float) = 1.2
        _Drift ("Dust drift speed", Float) = 0.15
    }

    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
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
            float _EdgeSoftness;
            float _FarFade;
            float _NearFade;
            float _Drift;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float facing : TEXCOORD1;
                float eyeDist : TEXCOORD2;
                float3 world : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 n = normalize(UnityObjectToWorldNormal(v.normal));
                float3 toEye = _WorldSpaceCameraPos - world;
                o.eyeDist = length(toEye);
                o.facing = abs(dot(n, toEye / max(o.eyeDist, 1e-4)));
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.world = world;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float edge = smoothstep(0, _EdgeSoftness, i.uv.x) * smoothstep(0, _EdgeSoftness, 1 - i.uv.x);
                float along = pow(saturate(1 - i.uv.y), _FarFade) * smoothstep(0, 0.06, i.uv.y + 0.02);
                float facing = smoothstep(0.05, 0.6, i.facing);
                float near = saturate(i.eyeDist / max(_NearFade, 0.01));
                // Slow drifting streaks so the beam doesn't look like a flat gradient.
                float streak = 0.8 + 0.2 * sin(i.world.x * 3.1 + i.world.z * 2.3 + _Time.y * _Drift * 6.283);
                float a = _Color.a * edge * along * facing * near * streak;
                return fixed4(_Color.rgb, saturate(a));
            }
            ENDCG
        }
    }
}
