// Shell-damaged masonry for the V2 ruins: weathered plaster over red brick. Where the plaster has fallen off
// (noise mask driven by vertex colour r = plaster cover) the brick shows through; broken tops, jambs and holes
// are generated with r = 0 so they read as raw brick. Vertex colour g = soot (fire/blast blackening from below
// and around openings), b = AO. UV0 is in metres (u along the wall, v up) so textures keep real scale.
Shader "FieldMedic/RuinWall"
{
    Properties
    {
        _PlasterMap ("Plaster albedo", 2D) = "white" {}
        [Normal] _PlasterNormal ("Plaster normal", 2D) = "bump" {}
        _BrickMap ("Brick albedo", 2D) = "white" {}
        [Normal] _BrickNormal ("Brick normal", 2D) = "bump" {}
        _PlasterTint ("Plaster tint", Color) = (1,1,1,1)
        _BrickTint ("Brick tint", Color) = (1,1,1,1)
        _Tile ("Metres per tile (plaster, brick)", Vector) = (2.2, 1.3, 0, 0)
        _BumpScale ("Normal strength", Float) = 1
        _Smoothness ("Smoothness", Range(0,1)) = 0.12
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _PlasterMap_ST;
            half4 _PlasterTint;
            half4 _BrickTint;
            float4 _Tile;
            half _BumpScale;
            half _Smoothness;
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

            TEXTURE2D(_PlasterMap); SAMPLER(sampler_PlasterMap);
            TEXTURE2D(_PlasterNormal);
            TEXTURE2D(_BrickMap);
            TEXTURE2D(_BrickNormal);

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
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(v.normalOS, v.tangentOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.uv = v.uv;
                o.normalWS = n.normalWS;
                o.tangentWS = half4(n.tangentWS, v.tangentOS.w * GetOddNegativeScale());
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
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 uvP = i.uv / _Tile.x;
                float2 uvB = i.uv / _Tile.y;
                half3 plaster = SAMPLE_TEXTURE2D(_PlasterMap, sampler_PlasterMap, uvP).rgb * _PlasterTint.rgb;
                half3 brick = SAMPLE_TEXTURE2D(_BrickMap, sampler_PlasterMap, uvB).rgb * _BrickTint.rgb;
                half3 nP = UnpackNormalScale(SAMPLE_TEXTURE2D(_PlasterNormal, sampler_PlasterMap, uvP), _BumpScale);
                half3 nB = UnpackNormalScale(SAMPLE_TEXTURE2D(_BrickNormal, sampler_PlasterMap, uvB), _BumpScale);

                // plaster loss: big flakes (low freq) with chipped edges (high freq), more where r is low
                float2 q = i.positionWS.xz + i.positionWS.y * 0.7;
                half flakes = vnoise(i.uv * 1.3 + q * 0.05) * 0.65 + vnoise(i.uv * 6.0) * 0.35;
                half cover = i.color.r;
                half pw = saturate((flakes - (1.0 - cover) * 0.9 - 0.12) * 9.0);
                // a dark rim where the plaster edge breaks (thickness shadow)
                half rim = saturate(1.0 - abs(pw - 0.5) * 2.2) * 0.35;

                half3 albedo = lerp(brick, plaster, pw) * (1.0 - rim);
                half3 nTS = normalize(lerp(nB, nP, pw));

                // soot + grime: vertex soot, streaks running down from the broken top
                half streak = vnoise(float2(i.uv.x * 7.0, i.uv.y * 0.35)) ;
                half soot = saturate(i.color.g * (0.75 + streak * 0.5));
                albedo *= lerp(1.0, 0.18, soot);
                albedo *= lerp(0.82, 1.0, saturate(i.uv.y * 1.5)); // splash dirt at the foot

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
                s.smoothness = _Smoothness;
                s.normalTS = nTS;
                s.occlusion = lerp(0.45, 1.0, i.color.b);

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
