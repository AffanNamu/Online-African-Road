// An unlit opaque surface that is its own light: the lit face of a street luminaire, a lit window, a stall bulb, a mast beacon, a vehicle's head or tail lamp.
// _BaseColor is what it looks like unlit (scaled by _Day so an unlit lamp is not bright at night); _EmissionColor is added on top and may exceed 1 (HDR). The game sets _EmissionColor from the
// lamp factor (ARO.NetCore.NightLighting), so a whole city's lamps are a few material writes per frame. It does not depend on the _EMISSION shader variant of the Lit shader, which a player build strips
// when no asset in the build uses it. Written like ARO/SkyGradient (plain unlit pass, built-in include), which URP draws through SRPDefaultUnlit.
Shader "ARO/LampEmit"
{
    Properties
    {
        _BaseColor ("Unlit colour", Color) = (0.62, 0.64, 0.66, 1)
        _EmissionColor ("Lit colour (HDR)", Color) = (0, 0, 0, 1)
        _Day ("Daylight scale of the unlit colour", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            float4 _BaseColor;
            float4 _EmissionColor;
            float _Day;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                return float4(_BaseColor.rgb * _Day + _EmissionColor.rgb, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
