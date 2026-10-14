// Cheap cloth flag: a subdivided plane (x = 0 at the pole → 1 at the fly end, y up, UV0 = texture rect) waved in
// the vertex shader by travelling sine waves whose amplitude grows from the pole outward; the normal is the
// analytic derivative so the folds shade correctly. Two-sided (back faces flip the normal). No textures beyond
// the flag itself; lit by the main light + SH ambient + fog. Wind strength/speed are global so weather can drive it.
Shader "FieldMedic/FlagWave"
{
    Properties
    {
        _BaseMap ("Flag", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1,1,1,1)
        _Length ("Flag length (m)", Float) = 1.8
        _Amplitude ("Wave amplitude (m)", Float) = 0.12
        _Frequency ("Waves per length", Float) = 2.2
        _Speed ("Wave speed", Float) = 5.5
        _Droop ("Droop at the fly end (m)", Float) = 0.08
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            float _Length;
            float _Amplitude;
            float _Frequency;
            float _Speed;
            float _Droop;
        CBUFFER_END

        // returns displaced object-space position and the normal of the waved surface
        void Wave(float3 p, out float3 pos, out float3 nrm)
        {
            float x = saturate(p.x / _Length);          // 0 at pole → 1 at fly end
            float k = _Frequency * 2.0 * PI / _Length;
            float t = _Time.y * _Speed;
            float a = _Amplitude * (0.15 + 0.85 * x);
            float ph = k * p.x - t + p.y * 1.3;
            float z = a * (sin(ph) + 0.35 * sin(ph * 2.3 + 1.7));
            float dz = a * k * (cos(ph) + 0.35 * 2.3 * cos(ph * 2.3 + 1.7)); // ∂z/∂x
            float dzy = a * 1.3 * (cos(ph) + 0.35 * 2.3 * cos(ph * 2.3 + 1.7)); // ∂z/∂y
            pos = float3(p.x - abs(z) * 0.15, p.y - _Droop * x * x, p.z + z);
            nrm = normalize(float3(-dz, -dzy, 1.0));
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half fog : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 pos, n;
                Wave(v.positionOS.xyz, pos, n);
                o.positionWS = TransformObjectToWorld(pos);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(n);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                half3 n = normalize(i.normalWS) * IS_FRONT_VFACE(face, 1.0, -1.0);
                Light l = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half ndl = saturate(dot(n, l.direction));
                half wrap = saturate(dot(n, l.direction) * 0.5 + 0.5) * 0.35; // cloth transmits a little light
                half3 col = albedo * (l.color * (ndl * l.shadowAttenuation + wrap) + max(SampleSH(n), half3(0.05, 0.05, 0.05)));
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float3 _LightDirection;
            struct Attributes { float4 positionOS : POSITION; };
            float4 vert(Attributes v) : SV_POSITION
            {
                float3 pos, n;
                Wave(v.positionOS.xyz, pos, n);
                float3 ws = TransformObjectToWorld(pos);
                float3 nws = TransformObjectToWorldNormal(n);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(ws, nws, _LightDirection));
            #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
            #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return cs;
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
