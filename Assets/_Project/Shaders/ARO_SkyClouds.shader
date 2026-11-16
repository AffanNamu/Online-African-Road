// The sky of the game (owner direction of 2026-11-15: "look at the sky, look at the colour: that is how realistic I want it"): the three-stop gradient of ARO/SkyGradient (colours from
// ARO.NetCore.SkyGradientModel) with real cloud shapes on it, all computed per pixel from noise (no textures):
//   * a bank of cumulus heaps above the horizon (flat bases, billowing tops, sunlit sides, grey-blue bases, sinking into the horizon haze),
//   * smaller cumulus and thin high cloud overhead,
//   * the sun's glow and disc dimmed behind cloud, a silver lining towards the sun,
//   * stars at night.
// Cloud colours, cover and the star strength come from ARO.NetCore.SkyCloudModel, so the sky stays in step with the sun, the ambient light and the weather's cloud amount.
// The arithmetic was prototyped and tuned outside the game (the same formulas in Python, rendered to pictures) before it was written here. ARO_SKY_LOW (low quality tier) uses fewer noise octaves.
// Like ARO/SkyGradient it is a plain skybox pass for the built-in shader library that URP projects ship: it needs only the view direction.
Shader "ARO/SkyClouds"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.16, 0.40, 0.84, 1)
        _Mid ("Mid sky", Color) = (0.36, 0.62, 0.95, 1)
        _Horizon ("Horizon", Color) = (0.74, 0.84, 0.93, 1)
        _Ground ("Ground", Color) = (0.45, 0.47, 0.46, 1)
        _GlowColor ("Sun glow", Color) = (1, 0.95, 0.85, 1)
        _SunDir ("Direction to the sun", Vector) = (0, 1, 0, 0)
        _GlowStrength ("Glow strength", Float) = 0.2
        _GlowPower ("Glow tightness", Float) = 6
        _DiscStrength ("Sun disc", Float) = 1
        _MidHeight ("Mid stop height", Float) = 0.22
        _HeightExp ("Height curve", Float) = 1
        _CloudLight ("Cloud, sunlit", Color) = (0.93, 0.93, 0.92, 1)
        _CloudDark ("Cloud, shaded", Color) = (0.50, 0.57, 0.70, 1)
        _CloudCover ("Cloud cover", Float) = 0.62
        _CloudOpacity ("Cloud opacity", Float) = 1
        _CloudTime ("Cloud drift time", Float) = 0
        _StarStrength ("Stars", Float) = 0
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
            #pragma multi_compile _ ARO_SKY_LOW
            #include "UnityCG.cginc"

            float4 _Zenith, _Mid, _Horizon, _Ground, _GlowColor, _CloudLight, _CloudDark;
            float4 _SunDir;
            float _GlowStrength, _GlowPower, _DiscStrength, _MidHeight, _HeightExp, _CloudCover, _CloudOpacity, _CloudTime, _StarStrength;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            // smoothstep written out: the GLSL one is undefined when the first edge is above the second
            float ss(float a, float b, float x) { float t = saturate((x - a) / (b - a)); return t * t * (3.0 - 2.0 * t); }

            float hash3(float3 p) { return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453); }

            float noise3(float3 p)
            {
                float3 i = floor(p); float3 f = p - i; float3 u = f * f * (3.0 - 2.0 * f);
                float x00 = lerp(hash3(i), hash3(i + float3(1, 0, 0)), u.x);
                float x10 = lerp(hash3(i + float3(0, 1, 0)), hash3(i + float3(1, 1, 0)), u.x);
                float x01 = lerp(hash3(i + float3(0, 0, 1)), hash3(i + float3(1, 0, 1)), u.x);
                float x11 = lerp(hash3(i + float3(0, 1, 1)), hash3(i + float3(1, 1, 1)), u.x);
                return lerp(lerp(x00, x10, u.y), lerp(x01, x11, u.y), u.z);
            }

            // fractal noise, normalised to 0..1; each octave is shifted so the layers do not line up
            float fbm2(float3 p) { float s = 0.5 * noise3(p); p = p * 2.03 + float3(17.3, 9.1, 5.7); s += 0.25 * noise3(p); return s / 0.75; }
            float fbm3(float3 p) { float s = 0.5 * noise3(p); p = p * 2.03 + float3(17.3, 9.1, 5.7); s += 0.25 * noise3(p); p = p * 2.03 + float3(17.3, 9.1, 5.7); s += 0.125 * noise3(p); return s / 0.875; }
            float fbm4(float3 p)
            {
                float s = 0.5 * noise3(p); p = p * 2.03 + float3(17.3, 9.1, 5.7);
                s += 0.25 * noise3(p); p = p * 2.03 + float3(17.3, 9.1, 5.7);
                s += 0.125 * noise3(p); p = p * 2.03 + float3(17.3, 9.1, 5.7);
                s += 0.0625 * noise3(p);
                return s / 0.9375;
            }
            float fbm5(float3 p)
            {
                float s = 0.5 * noise3(p); p = p * 2.03 + float3(17.3, 9.1, 5.7);
                s += 0.25 * noise3(p); p = p * 2.03 + float3(17.3, 9.1, 5.7);
                s += 0.125 * noise3(p); p = p * 2.03 + float3(17.3, 9.1, 5.7);
                s += 0.0625 * noise3(p); p = p * 2.03 + float3(17.3, 9.1, 5.7);
                s += 0.03125 * noise3(p);
                return s / 0.96875;
            }
            #ifdef ARO_SKY_LOW
                #define FBM_SHAPE fbm4      // four octaves: with three the heaps read as smeared blobs (CI run 349, low-tier pictures)
                #define FBM_LIGHT fbm2
            #else
                #define FBM_SHAPE fbm5
                #define FBM_LIGHT fbm3
            #endif

            float4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 sun = normalize(_SunDir.xyz);
                float dy = d.y;

                // the gradient: horizon, mid sky, zenith
                float h = pow(saturate(dy), _HeightExp);
                float lowT = ss(0.0, 1.0, saturate(h / max(_MidHeight, 0.001)));
                float highT = ss(0.0, 1.0, saturate((h - _MidHeight) / max(1.0 - _MidHeight, 0.001)));
                float3 c = h < _MidHeight ? lerp(_Horizon.rgb, _Mid.rgb, lowT) : lerp(_Mid.rgb, _Zenith.rgb, highT);
                float s = saturate(dot(d, sun));

                // stars: one in a few hundred cells of the direction grid holds a point
                if (_StarStrength > 0.001)
                {
                    float3 q = d * 190.0; float3 cell = floor(q);
                    float star = ss(0.9965, 1.0, hash3(cell)) * ss(0.42, 0.0, length(q - cell - 0.5)) * ss(0.02, 0.25, dy);
                    c += _StarStrength * star * float3(0.9, 0.95, 1.0);
                }

                // the cumulus bank: heaps with flat bases, in direction space (seamless all the way round)
                const float bankScale = 2.7, bankThresh = 0.50, bankFall = 1.05;
                float3 p = d * float3(bankScale, bankScale * 1.55, bankScale) + float3(_CloudTime * 0.004, 0.0, _CloudTime * 0.0015);
                float n = FBM_SHAPE(p);
                #ifndef ARO_SKY_LOW
                    n += (fbm3(p * 3.3 + 4.1) - 0.5) * 0.10;                                              // cauliflower detail on the heaps
                #endif
                float baseEl = 0.05 + (noise3(float3(d.x, 0.0, d.z) * 2.1 + 31.7) - 0.5) * 0.03;             // the cloud base is not one ruled line
                float above = dy - baseEl;
                float dens = ss(bankThresh, bankThresh + 0.07, n - above * bankFall + _CloudCover * 0.25) * ss(-0.004, 0.03, above);
                float sunSide = saturate((n - FBM_LIGHT(p + sun * 0.42)) * 3.2 + 0.52);                        // thinner towards the sun: this side faces it
                float topSide = saturate(above * bankFall * 1.6);                                              // tops are brighter than bases
                float shade = saturate(sunSide * 0.72 + topSide * 0.48 - 0.06);
                float3 cc = lerp(_CloudDark.rgb, _CloudLight.rgb, shade);
                cc += _GlowColor.rgb * pow(s, 10.0) * (1.0 - dens) * 0.5 * _GlowStrength;                  // silver lining towards the sun
                float haze = ss(0.0, 0.10, dy);                                                            // the bank sinks into the horizon haze
                c = lerp(c, lerp(_Horizon.rgb, cc, 0.35 + 0.65 * haze), dens * _CloudOpacity);

                // overhead: smaller cumulus on a plane, and thin high cloud
                float t = 1.0 / (max(dy, 0.0) + 0.09);
                float2 uv = d.xz * t * 0.62;
                float3 q3 = float3(uv + float2(_CloudTime * 0.01, _CloudTime * 0.004), 3.7);
                float m = FBM_SHAPE(q3);
                float od = ss(0.60 - _CloudCover * 0.22, 0.74 - _CloudCover * 0.22, m) * ss(0.16, 0.34, dy);
                float ol = saturate((m - FBM_LIGHT(q3 + float3(sun.x, sun.z, 0.0) * 0.35)) * 3.0 + 0.55);
                c = lerp(c, lerp(_CloudDark.rgb, _CloudLight.rgb, ol), od * _CloudOpacity * 0.9);
                #ifndef ARO_SKY_LOW
                    float cir = fbm3(float3(uv * float2(0.22, 0.75) + 11.0, 9.2));
                    c = lerp(c, _CloudLight.rgb, ss(0.52, 0.85, cir) * 0.22 * ss(0.10, 0.4, dy) * _CloudOpacity);
                #endif

                // the sun: a broad glow and a disc, dimmed behind cloud
                float glow = pow(s, _GlowPower) * _GlowStrength + pow(s, _GlowPower * 8.0) * _GlowStrength * 0.6;
                float disc = ss(0.99955, 0.99985, s) * _DiscStrength;
                c += _GlowColor.rgb * (glow + disc) * (1.0 - 0.75 * max(dens, od));

                c = lerp(c, _Ground.rgb, ss(0.0, -0.08, dy));
                return float4(c, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
