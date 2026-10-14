// Mid-distance crowd soldiers (GPU instanced baked meshes): vertex colour albedo (baked per material from the
// soldier's textures), half-Lambert main light + SH ambient, distance fog. No textures, no shadows: at 60-200 m
// in haze the silhouette and the value read; this keeps 44+ soldiers in a handful of instanced draws.
Shader "FieldMedic/CrowdLit"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; half3 col : TEXCOORD0; float fog : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                half3 n = normalize(TransformObjectToWorldNormal(v.normalOS));
                o.positionCS = TransformWorldToHClip(ws);
                Light l = GetMainLight();
                half ndl = saturate(dot(n, l.direction) * 0.5 + 0.5);
                half3 light = l.color * ndl * 0.8 + SampleSH(n);
                o.col = v.color.rgb * _Tint.rgb * light;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                return half4(MixFog(i.col, i.fog), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
