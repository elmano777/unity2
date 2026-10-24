// Swapped onto a minion the moment it dies so it can fade out: the glTF materials it wears are
// opaque Shader Graphs, and turning them transparent at runtime needs keyword variants a Quest build
// strips. Texture × colour × vertex colour, lit by the main light plus a flat ambient, and _Alpha
// driven from MinionAgent. A depth-only pass first so the body's own back parts don't show through
// while it fades. Stereo macros so it renders in both eyes on Quest.
Shader "Deadlock/MinionFade"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1, 1, 1, 1)
        _Alpha ("Alpha", Range(0, 1)) = 1
        _Ambient ("Ambient", Range(0, 1)) = 0.45
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        CGINCLUDE
        #include "UnityCG.cginc"

        sampler2D _BaseMap;
        float4 _BaseMap_ST;
        fixed4 _BaseColor;
        float _Alpha;
        float _Ambient;

        // Set globally by URP for the main directional light.
        float4 _MainLightPosition;
        half4 _MainLightColor;

        struct appdata
        {
            float4 vertex : POSITION;
            float3 normal : NORMAL;
            float2 uv : TEXCOORD0;
            fixed4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct v2f
        {
            float4 pos : SV_POSITION;
            float2 uv : TEXCOORD0;
            float3 normal : TEXCOORD1;
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
            o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
            o.normal = UnityObjectToWorldNormal(v.normal);
            o.color = v.color;
            return o;
        }
        ENDCG

        Pass
        {
            ZWrite On
            ColorMask 0

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragDepth
            #pragma multi_compile_instancing

            fixed4 fragDepth (v2f i) : SV_Target
            {
                return 0;
            }
            ENDCG
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                fixed4 albedo = tex2D(_BaseMap, i.uv) * _BaseColor * i.color;
                float diffuse = saturate(dot(normalize(i.normal), _MainLightPosition.xyz));
                float3 light = _Ambient + _MainLightColor.rgb * diffuse * (1 - _Ambient);
                return fixed4(albedo.rgb * light, _Alpha);
            }
            ENDCG
        }
    }
}
