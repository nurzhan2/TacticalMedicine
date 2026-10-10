// Casualty body/uniform shader for URP (mobile-friendly PBR via UniversalFragmentPBR).
// Extra over URP Lit:
//   - up to 4 blood soak spheres (world xyz + radius) with a noisy organic edge: fresh wet dark-red centre,
//     oxidised brown-black rim, higher smoothness while wet (driven by BloodSoak via MaterialPropertyBlock)
//   - skin pallor / cyanosis / sweat applied through _SkinMask (R = skin) — the current Swat model has no
//     exposed skin (balaclava), so the default mask is black; a face model can plug in later.
Shader "FieldMedic/CasualtyLit"
{
    Properties
    {
        [MainTexture] _BaseMap ("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor ("Color", Color) = (1,1,1,1)
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1.0
        _MetallicGlossMap ("Metallic (R) Smoothness (A)", 2D) = "black" {}
        _Smoothness ("Smoothness Scale", Range(0,1)) = 0.6
        _SkinMask ("Skin Mask (R)", 2D) = "black" {}
        _SoakNoise ("Soak Noise", 2D) = "gray" {}
        _SoakNoiseScale ("Soak Noise Tiling (world)", Float) = 9
        _BloodFresh ("Blood Fresh", Color) = (0.30, 0.015, 0.012, 1)
        _BloodOld ("Blood Old", Color) = (0.07, 0.012, 0.008, 1)
        _SoakSphere0 ("Soak 0", Vector) = (0,-1000,0,0)
        _SoakSphere1 ("Soak 1", Vector) = (0,-1000,0,0)
        _SoakSphere2 ("Soak 2", Vector) = (0,-1000,0,0)
        _SoakSphere3 ("Soak 3", Vector) = (0,-1000,0,0)
        _Pallor ("Pallor", Range(0,1)) = 0
        _Cyanosis ("Cyanosis", Range(0,1)) = 0
        _Sweat ("Sweat", Range(0,1)) = 0
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
            float _SoakNoiseScale;
            half4 _BloodFresh;
            half4 _BloodOld;
            float4 _SoakSphere0;
            float4 _SoakSphere1;
            float4 _SoakSphere2;
            float4 _SoakSphere3;
            half _Pallor;
            half _Cyanosis;
            half _Sweat;
            // URP shadow/depth passes expect these
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
            TEXTURE2D(_MetallicGlossMap); SAMPLER(sampler_MetallicGlossMap);
            TEXTURE2D(_SkinMask); SAMPLER(sampler_SkinMask);
            TEXTURE2D(_SoakNoise); SAMPLER(sampler_SoakNoise);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
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
                return o;
            }

            half SoakAmount(float3 pos, float4 s, half noise)
            {
                if (s.w <= 0.0001) return 0;
                float d = distance(pos, s.xyz);
                float edge = s.w * (0.72 + 0.56 * noise);
                return saturate((edge - d) / max(0.012, s.w * 0.25));
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                half4 mg = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, i.uv);
                half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv), _BumpScale);
                half smooth = mg.a * _Smoothness;

                // --- blood soak (fabric wicks blood: noisy, follows the weave via uv noise + world noise)
                half nz = SAMPLE_TEXTURE2D(_SoakNoise, sampler_SoakNoise, i.uv * 7.0).r * 0.5
                        + SAMPLE_TEXTURE2D(_SoakNoise, sampler_SoakNoise, i.positionWS.xz * _SoakNoiseScale + i.positionWS.y * 3.1).r * 0.5;
                half soak = 0;
                soak = max(soak, SoakAmount(i.positionWS, _SoakSphere0, nz));
                soak = max(soak, SoakAmount(i.positionWS, _SoakSphere1, nz));
                soak = max(soak, SoakAmount(i.positionWS, _SoakSphere2, nz));
                soak = max(soak, SoakAmount(i.positionWS, _SoakSphere3, nz));
                if (soak > 0)
                {
                    // centre = fresh & wet, rim = darker oxidised and drier
                    half3 blood = lerp(_BloodOld.rgb, _BloodFresh.rgb, saturate(soak * 1.6 - 0.4));
                    half lumA = dot(albedo.rgb, half3(0.3, 0.59, 0.11));
                    half3 stained = blood * (0.55 + 0.9 * lumA);
                    albedo.rgb = lerp(albedo.rgb, stained, saturate(soak * 1.3));
                    smooth = lerp(smooth, 0.78, saturate(soak * 1.5 - 0.3));
                    nTS = normalize(lerp(nTS, half3(0, 0, 1), soak * 0.6)); // wet fabric: flatter weave
                }

                // --- skin state (only where the mask says skin)
                half skin = SAMPLE_TEXTURE2D(_SkinMask, sampler_SkinMask, i.uv).r;
                if (skin > 0)
                {
                    half l = dot(albedo.rgb, half3(0.3, 0.59, 0.11));
                    half3 pale = lerp(albedo.rgb, half3(l, l, l) * half3(1.05, 1.0, 0.95) * 1.15, _Pallor * 0.8);
                    half3 blue = lerp(pale, pale * half3(0.7, 0.78, 1.05), _Cyanosis * 0.7);
                    albedo.rgb = lerp(albedo.rgb, blue, skin);
                    smooth = lerp(smooth, max(smooth, 0.65), _Sweat * skin);
                }

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
                s.albedo = albedo.rgb;
                s.alpha = 1;
                s.metallic = mg.r;
                s.smoothness = smooth;
                s.normalTS = nTS;
                s.occlusion = 1;
                s.specular = half3(0, 0, 0);

                half4 col = UniversalFragmentPBR(inputData, s);
                col.rgb = MixFog(col.rgb, inputData.fogCoord);
                return col;
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
    FallBack "Universal Render Pipeline/Lit"
}
