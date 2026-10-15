// A three-stop sky gradient (horizon, mid sky, zenith) with a ground colour, a broad glow and a small disc around the sun. Colours come from ARO.NetCore.SkyGradientModel.
// It replaces the procedural skybox only when the sky is switched to the gradient (see TimeOfDay); nothing else uses it. Written for the built-in shader library that URP projects still ship:
// a skybox pass needs no lighting, only the view direction, which a skybox cube provides as its vertex position.
Shader "ARO/SkyGradient"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.20, 0.44, 0.82, 1)
        _Mid ("Mid sky", Color) = (0.50, 0.68, 0.88, 1)
        _Horizon ("Horizon", Color) = (0.72, 0.83, 0.92, 1)
        _Ground ("Ground", Color) = (0.45, 0.47, 0.46, 1)
        _GlowColor ("Sun glow", Color) = (1, 0.9, 0.7, 1)
        _SunDir ("Direction to the sun", Vector) = (0, 1, 0, 0)
        _GlowStrength ("Glow strength", Float) = 0
        _GlowPower ("Glow tightness", Float) = 6
        _DiscStrength ("Sun disc", Float) = 0
        _MidHeight ("Mid stop height", Float) = 0.35
        _HeightExp ("Height curve", Float) = 0.5
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            fixed4 _Zenith, _Mid, _Horizon, _Ground, _GlowColor;
            float4 _SunDir;
            float _GlowStrength, _GlowPower, _DiscStrength, _MidHeight, _HeightExp;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = pow(saturate(d.y), _HeightExp);
                float lowT = smoothstep(0.0, 1.0, saturate(h / max(_MidHeight, 0.001)));
                float highT = smoothstep(0.0, 1.0, saturate((h - _MidHeight) / max(1.0 - _MidHeight, 0.001)));
                float3 c = h < _MidHeight ? lerp(_Horizon.rgb, _Mid.rgb, lowT) : lerp(_Mid.rgb, _Zenith.rgb, highT);
                c = lerp(c, _Ground.rgb, smoothstep(0.0, -0.08, d.y));
                float s = saturate(dot(d, normalize(_SunDir.xyz)));
                float glow = pow(s, _GlowPower) * _GlowStrength + pow(s, _GlowPower * 8.0) * _GlowStrength * 0.6;
                float disc = smoothstep(0.99955, 0.99985, s) * _DiscStrength;
                c += _GlowColor.rgb * (glow + disc);
                return fixed4(c, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
