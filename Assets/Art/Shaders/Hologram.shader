// Spirit-vision look for the heroes in the selector: see-through green body with a bright outline
// (fresnel) and scanlines drifting upwards. _Intensity rises while the player points at the hero.
Shader "Deadlock/Hologram"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.25, 1.2, 0.65, 1)
        _Intensity ("Intensity", Range(0, 3)) = 1
        _LineDensity ("Scanlines per metre", Float) = 40
        _LineSpeed ("Scanline speed", Float) = 0.4
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        Blend SrcAlpha One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Intensity;
            float _LineDensity;
            float _LineSpeed;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n = normalize(i.worldNormal);
                float3 v = normalize(_WorldSpaceCameraPos - i.worldPos);
                float rim = pow(1 - saturate(abs(dot(n, v))), 2);
                float lines = 0.55 + 0.45 * sin((i.worldPos.y - _Time.y * _LineSpeed) * _LineDensity * 6.2832);
                float alpha = saturate((0.12 + rim * 0.9) * lines * (0.5 + 0.5 * _Intensity));
                return fixed4(_Color.rgb * (0.6 + 0.4 * _Intensity), alpha);
            }
            ENDCG
        }
    }
}
