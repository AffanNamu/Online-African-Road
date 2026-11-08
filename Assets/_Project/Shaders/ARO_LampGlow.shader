// The glow layer of the night: ONE additive, unlit mesh that carries, for every lamp of the loaded chunks, a camera-facing halo (hot core, orange glow), a pool of light draped on the road under it and a
// long reflection streak for the wet road. Vertex data (see ARO.NetCore.GlowBuffers): uv0 = corner of the atlas tile, uv1 = (kind 0 halo / 1 pool / 2 streak, half size in metres for halos, intensity,
// beacon flag), colour = lamp colour. The lamp level, the wet weight and the distance fade are uniforms set once per frame by the game, so nothing is per lamp. Atlas: three square tiles side by side.
Shader "ARO/LampGlow"
{
    Properties
    {
        _Atlas ("Glow atlas", 2D) = "black" {}
        _Level ("Lamp level 0..1", Float) = 0
        _Beacon ("Beacon pulse 0..1", Float) = 1
        _Weights ("Halo, pool, streak weights", Vector) = (1, 1, 0, 0)
        _Fade ("Fade start, end in metres", Vector) = (300, 500, 0, 0)
        _HaloMin ("Smallest halo, metres per metre of distance", Float) = 0.004
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
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

            sampler2D _Atlas;
            float _Level;
            float _Beacon;
            float _HaloMin;
            float4 _Weights;
            float4 _Fade;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 data : TEXCOORD1;
                float4 color : COLOR;
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 tint : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float kind = v.data.x;
                float3 world = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
                float dist = distance(_WorldSpaceCameraPos, world);
                if (kind < 0.5)
                {
                    float2 c = v.uv * 2.0 - 1.0;
                    float size = max(v.data.y, dist * _HaloMin);
                    float3 camRight = UNITY_MATRIX_V[0].xyz;
                    float3 camUp = UNITY_MATRIX_V[1].xyz;
                    world += (camRight * c.x + camUp * c.y) * size;
                }
                o.pos = mul(UNITY_MATRIX_VP, float4(world, 1.0));
                o.uv = float2((v.uv.x + kind) / 3.0, v.uv.y);
                float weight = kind < 0.5 ? _Weights.x : (kind < 1.5 ? _Weights.y : _Weights.z);
                float level = _Level * lerp(1.0, _Beacon, v.data.w);
                float fade = saturate((_Fade.y - dist) / max(_Fade.y - _Fade.x, 1.0));
                o.tint = v.color.rgb * (v.data.z * level * weight * fade);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float m = tex2D(_Atlas, i.uv).r;
                return float4(i.tint * m, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
