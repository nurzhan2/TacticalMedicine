// Horizon impostors: every figure is a quad whose 4 vertices share the figure's base position; uv1 = (corner x
// -0.5..0.5, corner y 0..1, phase 0..1, speed m/s). The vertex shader billboards around Y toward the camera,
// drifts the figure toward -Z with wrap-around over _Travel metres (fading at both ends), and picks the run-cycle
// frame from the atlas (_Frames columns). Alpha-tested; fog is applied at a reduced distance (_FogScale) so the
// advancing lines read as dark silhouettes in the haze instead of vanishing completely. Zero CPU cost.
Shader "FieldMedic/Impostor"
{
    Properties
    {
        _MainTex ("Run sheet", 2D) = "white" {}
        _Tint ("Tint", Color) = (0.55,0.55,0.52,1)
        _Size ("Width, height (m)", Vector) = (0.9, 1.85, 0, 0)
        _Frames ("Frames", Float) = 8
        _Fps ("Frames per second at 2.6 m/s", Float) = 9
        _Travel ("Travel before wrap (m)", Float) = 26
        _FogScale ("Fog distance scale", Float) = 0.62
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.45
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" }
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
                half4 _Tint;
                float4 _Size;
                float _Frames, _Fps, _Travel, _FogScale, _Cutoff;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 data : TEXCOORD1; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; float fog : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 c = TransformObjectToWorld(v.positionOS.xyz);
                float phase = v.data.z, speed = v.data.w;
                float drift = fmod(_Time.y * speed + phase * _Travel, _Travel);
                c.z -= drift;
                float3 toCam = _WorldSpaceCameraPos - c; toCam.y = 0;
                float3 right = normalize(cross(float3(0,1,0), toCam + float3(1e-4,0,0)));
                float3 w = c + right * (v.data.x * _Size.x) + float3(0,1,0) * (v.data.y * _Size.y);
                o.positionCS = TransformWorldToHClip(w);
                float frame = floor(fmod(_Time.y * _Fps * speed / 2.6 + phase * 37.0, _Frames));
                o.uv = float2((frame + v.uv.x) / _Frames, v.uv.y);
                float fade = smoothstep(0.0, 3.0, drift) * smoothstep(_Travel, _Travel - 3.0, drift);
                o.color = v.color * _Tint;
                o.color.a *= fade;
                o.fog = ComputeFogFactor(o.positionCS.z * _FogScale);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                clip(t.a * i.color.a - _Cutoff);
                half3 col = t.rgb * i.color.rgb;
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
