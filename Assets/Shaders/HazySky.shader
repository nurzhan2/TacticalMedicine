// Overcast day sky: the Poly Haven equirect HDRI, with a haze band that fades the horizon into the scene fog
// colour so the 400 m ground's fogged edge melts into the sky (no visible horizon line), plus an optional
// smoky darkening toward the enemy side (battle smoke hanging over the front).
Shader "FieldMedic/HazySky"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("HDRI (equirect)", 2D) = "grey" {}
        _Exposure ("Exposure", Float) = 1
        _Tint ("Tint", Color) = (1,1,1,1)
        _Rotation ("Rotation (deg)", Float) = 0
        _HazeColor ("Haze colour (= fog colour)", Color) = (0.6,0.6,0.6,1)
        _HazeHeight ("Haze height (dir.y where haze ends)", Float) = 0.18
        _SmokeDir ("Smoke direction (xz)", Vector) = (0,0,1,0)
        _SmokeAmount ("Smoke darkening", Range(0,1)) = 0.25
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half _Exposure;
                half4 _Tint;
                float _Rotation;
                half4 _HazeColor;
                half _HazeHeight;
                float4 _SmokeDir;
                half _SmokeAmount;
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

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 d = normalize(i.dir);
                float s, c;
                sincos(radians(_Rotation), s, c);
                float3 r = float3(d.x * c - d.z * s, d.y, d.x * s + d.z * c);
                float2 uv = float2(atan2(r.x, r.z) / (2.0 * PI) + 0.5, asin(clamp(r.y, -1.0, 1.0)) / PI + 0.5);
                half3 sky = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uv, 0).rgb * _Tint.rgb * _Exposure;

                // smoke over the front: darker, browner sky low toward the enemy
                float2 sd = normalize(_SmokeDir.xz + 1e-4);
                half toward = saturate(dot(normalize(d.xz + 1e-4), sd) * 0.5 + 0.5);
                half low = saturate(1.0 - d.y * 2.2);
                sky = lerp(sky, sky * half3(0.62, 0.6, 0.57), _SmokeAmount * toward * toward * low);

                half haze = saturate(1.0 - max(d.y, 0.0) / _HazeHeight);
                haze = haze * haze * (3.0 - 2.0 * haze);
                sky = lerp(sky, _HazeColor.rgb, haze);
                return half4(sky, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
