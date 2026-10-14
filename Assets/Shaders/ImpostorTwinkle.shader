// Muzzle twinkles of the horizon impostor lines: tiny additive quads that drift with their figure (same uv1
// layout as FieldMedic/Impostor) and flash for a frame or two at random — distant rifles firing in the haze.
Shader "FieldMedic/ImpostorTwinkle"
{
    Properties
    {
        _MainTex ("Glow", 2D) = "white" {}
        _Color ("Colour", Color) = (1,0.75,0.45,1)
        _Size ("Size (m)", Float) = 0.9
        _Travel ("Travel before wrap (m)", Float) = 26
        _Rate ("Flash rate", Float) = 0.035
        _FogScale ("Fog distance scale", Float) = 0.5
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                float _Size, _Travel, _Rate, _FogScale;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 data : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half k : TEXCOORD1; float fog : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            float hash(float n) { return frac(sin(n) * 43758.5453); }
            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 c = TransformObjectToWorld(v.positionOS.xyz);
                float phase = v.data.z, speed = v.data.w;
                float drift = fmod(_Time.y * speed + phase * _Travel, _Travel);
                c.z -= drift;
                c.y += 1.15;
                float3 toCam = _WorldSpaceCameraPos - c;
                float3 right = normalize(cross(float3(0,1,0), toCam + float3(1e-4,0,0)));
                float3 up = normalize(cross(toCam, right));
                float3 w = c + (right * v.data.x + up * (v.data.y - 0.5)) * _Size;
                o.positionCS = TransformWorldToHClip(w);
                o.uv = v.uv;
                float tick = floor(_Time.y * 14.0);
                o.k = step(1.0 - _Rate, hash(phase * 911.0 + tick * 0.731));
                o.fog = ComputeFogFactor(o.positionCS.z * _FogScale);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a * i.k;
                half3 col = _Color.rgb * a * 2.5 * ComputeFogIntensity(i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
