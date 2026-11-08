// Standing water on the road, drawn as flat alpha-blended patches (COSMETIC: no depth, no splash, no effect on grip; the engine has no water layer). Each patch has an appearance threshold in uv1.x: it fades
// in once the road is wetter than that (_Wet, set from WeatherSystem.Wetness). The colour is dark water plus the reflected sky (_Sky, set from the lighting state), so a puddle reads as a bluish sheen at night
// and as a bright patch of sky by day. The lamp reflections on it come from the glow layer, which is drawn after it.
Shader "ARO/Puddle"
{
    Properties
    {
        _Wet ("Road wetness 0..1", Float) = 0
        _Tint ("Water colour (rgb) and strength (a)", Color) = (0.03, 0.04, 0.06, 0.7)
        _Sky ("Reflected sky", Color) = (0.05, 0.07, 0.10, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+5" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        ZTest LEqual
        Offset -2, -2
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            float _Wet;
            float4 _Tint;
            float4 _Sky;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float2 data : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float vis : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.vis = smoothstep(v.data.x, v.data.x + 0.12, _Wet);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 c = i.uv * 2.0 - 1.0;
                float r = length(c);
                float edge = 1.0 - smoothstep(0.55, 1.0, r);
                float3 col = _Tint.rgb + _Sky.rgb * (1.0 - 0.4 * r);
                return float4(col, edge * i.vis * _Tint.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
