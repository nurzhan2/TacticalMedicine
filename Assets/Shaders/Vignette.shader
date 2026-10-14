// Head-locked suppression vignette: darkens (slightly desaturated) toward the edge of the view. A quad in front
// of the eye; _Amount 0..1 drives the radius. Comfort-safe: no blur, no pulsing, capped at ~55 % darkness.
Shader "FieldMedic/Vignette"
{
    Properties
    {
        _Amount ("Amount", Range(0,1)) = 0
        _Color ("Edge colour", Color) = (0.05,0.04,0.035,1)
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half _Amount;
                half4 _Color;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float2 d = (i.uv - 0.5) * 2.0;
                float r = length(d);
                float inner = lerp(1.25, 0.55, _Amount);
                half a = smoothstep(inner, inner + 0.55, r) * saturate(_Amount * 1.4) * 0.55;
                return half4(_Color.rgb, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
