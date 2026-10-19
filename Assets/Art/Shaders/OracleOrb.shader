// The seer's crystal ball: a bright rim (fresnel) plus slow swirling mist inside, both green.
// _Energy goes up when the player hovers or strikes the orb (driven by OracleOrb.cs).
Shader "Deadlock/OracleOrb"
{
    Properties
    {
        [HDR] _RimColor ("Rim", Color) = (0.35, 1.4, 0.7, 1)
        [HDR] _MistColor ("Mist", Color) = (0.1, 0.9, 0.45, 1)
        _Energy ("Energy", Range(0, 3)) = 1
        _SwirlSpeed ("Swirl speed", Float) = 0.6
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

            fixed4 _RimColor;
            fixed4 _MistColor;
            float _Energy;
            float _SwirlSpeed;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 objPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 viewDir : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.objPos = v.vertex.xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = WorldSpaceViewDir(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n = normalize(i.worldNormal);
                float3 v = normalize(i.viewDir);
                float rim = pow(1 - saturate(dot(n, v)), 2.5);

                // Cheap swirl: layered sines over the object-space position, turning with time.
                float t = _Time.y * _SwirlSpeed;
                float3 p = i.objPos * 6;
                float swirl = sin(p.x * 1.3 + t * 2 + sin(p.y * 1.7 - t)) * sin(p.y * 1.1 - t * 1.5 + sin(p.z * 1.9 + t));
                float mist = saturate(0.35 + 0.45 * swirl) * (1 - rim * 0.5);

                float3 color = _RimColor.rgb * rim * (0.6 + 0.4 * _Energy) + _MistColor.rgb * mist * 0.6 * _Energy;
                float alpha = saturate(rim + mist * 0.55) * saturate(0.4 + 0.3 * _Energy);
                return fixed4(color, alpha);
            }
            ENDCG
        }
    }
}
