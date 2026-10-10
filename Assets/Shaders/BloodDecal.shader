// Alpha-blended wet blood decal for URP (ground pools, wound decals). Cheap: Lambert + Blinn-Phong highlight
// from the main light and additional lights (red headlamp, flares, explosion flashes) + SH ambient.
// Wetness = glossy highlight; _Dry darkens & mattes (clotting/drying).
Shader "FieldMedic/BloodDecal"
{
    Properties
    {
        [MainTexture] _BaseMap ("Albedo (A = coverage)", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint", Color) = (1,1,1,1)
        _Gloss ("Wet gloss", Range(0,1)) = 0.85
        _Dry ("Dry", Range(0,1)) = 0
        _Alpha ("Alpha", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "ForwardBlood"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Gloss;
                half _Dry;
                half _Alpha;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half fog : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half3 Shade(Light l, half3 n, half3 vdir, half3 albedo, half gloss)
            {
                half ndl = saturate(dot(n, l.direction));
                half3 h = normalize(l.direction + vdir);
                half spec = pow(saturate(dot(n, h)), lerp(16, 220, gloss)) * gloss * 2.5;
                return l.color * l.distanceAttenuation * l.shadowAttenuation * (albedo * ndl + spec * ndl);
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 t = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                half3 albedo = t.rgb * lerp(1.0, 0.45, _Dry);
                half gloss = _Gloss * (1 - _Dry * 0.8);
                half3 n = normalize(i.normalWS);
                half3 vdir = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half3 col = SampleSH(n) * albedo;
                col += Shade(GetMainLight(), n, vdir, albedo, gloss);
                #if defined(_ADDITIONAL_LIGHTS)
                uint count = GetAdditionalLightsCount();
                #if USE_CLUSTER_LIGHT_LOOP
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                #endif
                LIGHT_LOOP_BEGIN(count)
                    Light l = GetAdditionalLight(lightIndex, i.positionWS);
                    col += Shade(l, n, vdir, albedo, gloss);
                LIGHT_LOOP_END
                #endif
                col = MixFog(col, i.fog);
                return half4(col, saturate(t.a * _Alpha));
            }
            ENDHLSL
        }
    }
}
