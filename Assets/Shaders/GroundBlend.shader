// Steppe ground for the V2 battlefield: three PBR layers (dry grass / bare dirt / burnt soil) blended by the
// vertex colours the terrain generator writes (r = dirt, g = burn, b = vertex AO), world-space UVs so chunks never
// show seams, a noise-broken blend edge and a slow macro tint drift that hides tiling at 50-200 m.
Shader "FieldMedic/GroundBlend"
{
    Properties
    {
        _GrassMap ("Grass albedo", 2D) = "white" {}
        [Normal] _GrassNormal ("Grass normal", 2D) = "bump" {}
        _DirtMap ("Dirt albedo", 2D) = "white" {}
        [Normal] _DirtNormal ("Dirt normal", 2D) = "bump" {}
        _BurnMap ("Burnt albedo", 2D) = "white" {}
        _GrassTint ("Grass tint", Color) = (1,1,1,1)
        _DirtTint ("Dirt tint", Color) = (1,1,1,1)
        _BurnTint ("Burn tint", Color) = (1,1,1,1)
        _Tile ("Metres per texture tile (grass, dirt, burn, macro)", Vector) = (3, 2.5, 3, 60)
        _BumpScale ("Normal strength", Float) = 1
        _Smoothness ("Smoothness", Range(0,1)) = 0.12
        _Macro ("Macro variation", Range(0,1)) = 0.45
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _GrassMap_ST;
            half4 _GrassTint;
            half4 _DirtTint;
            half4 _BurnTint;
            float4 _Tile;
            half _BumpScale;
            half _Smoothness;
            half _Macro;
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

            TEXTURE2D(_GrassMap); SAMPLER(sampler_GrassMap);
            TEXTURE2D(_GrassNormal);
            TEXTURE2D(_DirtMap);
            TEXTURE2D(_DirtNormal);
            TEXTURE2D(_BurnMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                half3 vertexLight : TEXCOORD3;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                o.vertexLight = VertexLighting(p.positionWS, o.normalWS);
                o.color = v.color;
                return o;
            }

            // sine-free hash (D. Hoskins): frac(sin(big)*43758) degenerates into per-pixel garbage on GPUs with

            // reduced sin() precision for large arguments (the 'pixel noise' on the ground, supervisor 15.10 #1)

            float Hash21(float2 p)

            {

                float3 p3 = frac(float3(p.xyx) * 0.1031);

                p3 += dot(p3, p3.yzx + 33.33);

                return frac((p3.x + p3.y) * p3.z);

            }


            float vnoise(float2 x)
            {
                float2 i = floor(x); float2 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 w = i.positionWS.xz;
                // rotate the dirt/burn UVs so the three tilings never line up
                float2 uvG = w / _Tile.x;
                float2 uvD = float2(w.x * 0.8 - w.y * 0.6, w.x * 0.6 + w.y * 0.8) / _Tile.y;
                float2 uvB = float2(w.y, -w.x) / _Tile.z;

                // soft, large-scale breakup of the blend edges (a few metres), only a touch of fine detail
                half n1 = vnoise(w * 0.22);
                half n2 = vnoise(w * 0.9 + 13.1);
                half breakup = (n1 * 0.8 + n2 * 0.2) - 0.5;

                half dirtW = smoothstep(0.0, 1.0, saturate((i.color.r - 0.5 + breakup * 0.6) * 1.8 + 0.5));
                half burnW = smoothstep(0.0, 1.0, saturate((i.color.g - 0.5 + breakup * 0.5) * 1.8 + 0.5));

                half3 grass = SAMPLE_TEXTURE2D(_GrassMap, sampler_GrassMap, uvG).rgb * _GrassTint.rgb;
                half3 dirt = SAMPLE_TEXTURE2D(_DirtMap, sampler_GrassMap, uvD).rgb * _DirtTint.rgb;
                half3 burn = SAMPLE_TEXTURE2D(_BurnMap, sampler_GrassMap, uvB).rgb * _BurnTint.rgb;
                half3 nG = UnpackNormalScale(SAMPLE_TEXTURE2D(_GrassNormal, sampler_GrassMap, uvG), _BumpScale);
                half3 nD = UnpackNormalScale(SAMPLE_TEXTURE2D(_DirtNormal, sampler_GrassMap, uvD), _BumpScale);

                half3 albedo = lerp(lerp(grass, dirt, dirtW), burn, burnW);
                half3 nTS = normalize(lerp(nG, nD, saturate(dirtW + burnW)));

                // macro drift: patches of paler and darker, slightly greener steppe
                half m = vnoise(w / _Tile.w) * 0.6 + vnoise(w / (_Tile.w * 0.27) + 7.3) * 0.4;
                albedo *= lerp(1.0, lerp(half3(0.78, 0.8, 0.74), half3(1.12, 1.08, 1.0), m), _Macro);

                half ao = lerp(0.55, 1.0, i.color.b);

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                // ground is roughly horizontal: tangent frame from world axes
                half3 nW = normalize(i.normalWS);
                half3 t = normalize(cross(half3(0, 0, 1), nW));
                half3 b = cross(nW, t);
                inputData.normalWS = normalize(nTS.x * t + nTS.y * b + nTS.z * nW);
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
                s.smoothness = _Smoothness * (1.0 - burnW * 0.5);
                s.normalTS = nTS;
                s.occlusion = ao;

                half4 col = UniversalFragmentPBR(inputData, s);
                col.rgb = MixFog(col.rgb, inputData.fogCoord);
                return col;
            }
            ENDHLSL
        }

        // no ShadowCaster: a flat steppe casting into the 32 m shadow map only costs 51k triangles per frame
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
    FallBack "Universal Render Pipeline/Lit"
}
