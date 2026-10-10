// Procedural night sky for the front line: deep blue-black zenith, faint stars (twinkling), thin cloud
// veil, a warm burning glow low on the horizon at a few azimuths (towns / fires at the front) and a cool moon
// halo. Single pass, no textures, cheap enough for Quest (skybox covers few pixels in a trench anyway).
Shader "FieldMedic/NightSky"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.004, 0.006, 0.014, 1)
        _HorizonColor ("Horizon", Color) = (0.035, 0.045, 0.07, 1)
        _GlowColor ("Fire glow", Color) = (0.55, 0.22, 0.06, 1)
        _GlowStrength ("Fire glow strength", Range(0, 3)) = 1
        _GlowAzimuths ("Glow azimuths (deg)", Vector) = (15, 70, -40, 160)
        _MoonDir ("Moon direction", Vector) = (-0.45, 0.3, 0.6, 0)
        _MoonColor ("Moon", Color) = (0.75, 0.8, 0.95, 1)
        _StarDensity ("Star density", Range(0, 1)) = 0.55
        _StarBrightness ("Star brightness", Range(0, 4)) = 1.4
        _CloudStrength ("Cloud veil", Range(0, 1)) = 0.6
        _FlashColor ("Flash (set by script)", Color) = (0, 0, 0, 0)
        _FlashDir ("Flash direction", Vector) = (0, 0.1, 1, 0)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor, _HorizonColor, _GlowColor, _MoonColor, _FlashColor;
                half _GlowStrength, _StarDensity, _StarBrightness, _CloudStrength;
                float4 _GlowAzimuths, _MoonDir, _FlashDir;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float hash3(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float noise3(float3 x)
            {
                float3 i = floor(x); float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(hash3(i), hash3(i + float3(1,0,0)), f.x),
                                 lerp(hash3(i + float3(0,1,0)), hash3(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(hash3(i + float3(0,0,1)), hash3(i + float3(1,0,1)), f.x),
                                 lerp(hash3(i + float3(0,1,1)), hash3(i + float3(1,1,1)), f.x), f.y), f.z);
            }

            float glowAt(float az, float target, float width)
            {
                float d = abs(az - target);
                d = min(d, 360.0 - d);
                return exp(-d * d / (width * width));
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;
                float up = saturate(h);

                // base gradient (slightly lighter haze at the horizon, ground side very dark)
                half3 col = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(up, 0.45));
                col = lerp(col, _HorizonColor.rgb * 0.25, saturate(-h * 6.0));

                // fire glow on the horizon at a few azimuths, flickering slowly
                float az = degrees(atan2(d.x, d.z));
                float t = _Time.y;
                float g = glowAt(az, _GlowAzimuths.x, 22) * (0.85 + 0.15 * sin(t * 0.7))
                        + glowAt(az, _GlowAzimuths.y, 14) * 0.6 * (0.8 + 0.2 * sin(t * 1.3 + 1.0))
                        + glowAt(az, _GlowAzimuths.z, 30) * 0.45
                        + glowAt(az, _GlowAzimuths.w, 18) * 0.35;
                float band = exp(-max(h + 0.01, 0.0) * 14.0) * step(-0.05, h);
                col += _GlowColor.rgb * g * band * _GlowStrength;

                // cloud veil: low frequency noise that hides stars and catches the glow / moon
                float3 cp = d / max(h + 0.15, 0.05) * 1.6 + float3(t * 0.004, 0, t * 0.002);
                float cloud = saturate(noise3(cp) * 0.65 + noise3(cp * 2.7) * 0.35 - 0.35) * _CloudStrength;
                cloud *= smoothstep(-0.02, 0.15, h);

                // stars: hashed cells on the sphere
                float3 sp = d * 220.0;
                float3 cell = floor(sp);
                float r = hash3(cell);
                float star = 0;
                if (r > 1.0 - _StarDensity * 0.06)
                {
                    float3 c = cell + 0.5 + (hash3(cell + 7.1) - 0.5) * 0.6;
                    float dist = length(sp - c);
                    float tw = 0.7 + 0.3 * sin(t * (2.0 + r * 5.0) + r * 40.0);
                    star = saturate(1.0 - dist * 2.2) * tw * (0.3 + 0.7 * frac(r * 91.3));
                }
                star *= smoothstep(0.02, 0.25, h) * (1.0 - cloud * 1.4);
                col += star * _StarBrightness * half3(0.8, 0.85, 1.0);

                // moon halo + disc
                float3 md = normalize(_MoonDir.xyz);
                float mdot = saturate(dot(d, md));
                col += _MoonColor.rgb * (pow(mdot, 64.0) * 0.08 + pow(mdot, 8.0) * 0.02);
                col += _MoonColor.rgb * smoothstep(0.99985, 0.99992, mdot) * 1.2;

                // clouds lit by glow and moon, slightly lighter than the sky
                half3 cloudCol = _HorizonColor.rgb * 1.6 + _GlowColor.rgb * g * 0.18 + _MoonColor.rgb * pow(mdot, 6.0) * 0.06;
                col = lerp(col, cloudCol, cloud * 0.55);

                // explosion / flare flash in a direction
                float fd = saturate(dot(d, normalize(_FlashDir.xyz)));
                col += _FlashColor.rgb * (pow(fd, 6.0) * 0.8 + 0.08) * (1.0 + cloud * 2.0);

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
