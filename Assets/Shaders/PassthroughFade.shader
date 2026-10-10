// Full-view quad in front of the eyes: multiplies scene colour and alpha by (1 - _Opacity), so the Quest
// compositor reveals the passthrough underlay proportionally (proximity passthrough near real walls).
Shader "FieldMedic/PassthroughFade"
{
    Properties { _Opacity ("Opacity", Range(0,1)) = 0 }
    SubShader
    {
        Tags { "Queue"="Overlay+100" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend Zero OneMinusSrcAlpha
        ZWrite Off ZTest Always Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half _Opacity;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v) { V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half4 frag(V i) : SV_Target { return half4(0, 0, 0, _Opacity); }
            ENDHLSL
        }
    }
    Fallback Off
}
