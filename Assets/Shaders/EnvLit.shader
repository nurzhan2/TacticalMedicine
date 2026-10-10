// Environment PBR for the trench (keyword-free, so materials generated from editor scripts always work):
// albedo (+ smoothness in alpha), tangent normal map, tint, vertex-colour AO baked by the mesh generator,
// large-scale world-space tint variation to break texture tiling, and wetness that darkens + glosses.
Shader "FieldMedic/EnvLit"
{
    Properties
    {
        _BaseMap ("Albedo (A = smoothness if _SmoothFromAlpha)", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1,1,1,1)
        [Normal] _BumpMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Normal strength", Float) = 1
        _Smoothness ("Smoothness", Range(0,1)) = 0.2
        _SmoothFromAlpha ("Smoothness from albedo alpha", Range(0,1)) = 0
        _Wetness ("Wetness", Range(0,1)) = 0
        _Variation ("World tint variation", Range(0,1)) = 0.3
        _AOStrength ("Vertex AO + wet mask (generated meshes only)", Range(0,1)) = 0
        _Cutoff ("Cutoff (shadow pass)", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _BumpScale;
            half _Smoothness;
            half _SmoothFromAlpha;
            half _Wetness;
            half _Variation;
            half _AOStrength;
            half _Cutoff;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 tangentWS : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                half3 vertexLight : TEXCOORD5;
                half4 color : COLOR;
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
                VertexNormalInputs n = GetVertexNormalInputs(v.normalOS, v.tangentOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.normalWS = n.normalWS;
                real sign = v.tangentOS.w * GetOddNegativeScale();
                o.tangentWS = half4(n.tangentWS, sign);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                o.vertexLight = VertexLighting(p.positionWS, n.normalWS);
                o.color = v.color;
                return o;
            }

            half vnoise(float2 x)
            {
                float2 i = floor(x); float2 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                float a = frac(sin(dot(i, float2(127.1, 311.7))) * 43758.55);
                float b = frac(sin(dot(i + float2(1, 0), float2(127.1, 311.7))) * 43758.55);
                float c = frac(sin(dot(i + float2(0, 1), float2(127.1, 311.7))) * 43758.55);
                float d = frac(sin(dot(i + float2(1, 1), float2(127.1, 311.7))) * 43758.55);
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half3 albedo = tex.rgb * _BaseColor.rgb;
                half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv), _BumpScale);
                half smooth = lerp(1.0, tex.a, _SmoothFromAlpha) * _Smoothness;

                // break tiling: slow world-space brightness/hue drift
                half v = vnoise(i.positionWS.xz * 0.18 + i.positionWS.y * 0.11) * 0.65 + vnoise(i.positionWS.xz * 0.7) * 0.35;
                albedo *= lerp(1.0, lerp(0.72, 1.22, v) * half3(1.0, 0.98 + 0.04 * v, 0.96 + 0.06 * v), _Variation);

                // vertex AO (r) and wetness mask (g) from the generator
                half ao = lerp(1.0, i.color.r, _AOStrength);
                half wet = saturate(_Wetness * lerp(1.0, i.color.g, _AOStrength)); // meshes without colours: _AOStrength 0
                albedo *= lerp(1.0, 0.55, wet);
                smooth = lerp(smooth, 0.82, wet * 0.85);
                nTS = normalize(lerp(nTS, half3(0, 0, 1), wet * 0.5));

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                half3 bitangent = i.tangentWS.w * cross(i.normalWS, i.tangentWS.xyz);
                half3x3 tbn = half3x3(i.tangentWS.xyz, bitangent, i.normalWS);
                inputData.normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(nTS, tbn));
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = i.fogFactor;
                inputData.vertexLighting = i.vertexLight;
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.alpha = 1;
                s.metallic = 0;
                s.smoothness = smooth;
                s.normalTS = nTS;
                s.occlusion = ao;
                s.specular = half3(0, 0, 0);

                half4 col = UniversalFragmentPBR(inputData, s);
                col.rgb = MixFog(col.rgb, inputData.fogCoord);
                return col;
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
    FallBack "Universal Render Pipeline/Lit"
}
