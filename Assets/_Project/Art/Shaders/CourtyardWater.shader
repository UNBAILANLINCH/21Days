// 现有 TitleWaterTransition 只服务 UI；扩展 ToonLit 会混入透明水面职责，故独立实现庭院水。
// shortcut: 用菲涅耳染色近似天空反光，需要清晰建筑倒影时再接反射方案。
Shader "21Days/CourtyardWater"
{
    Properties
    {
        _ShallowColor ("浅水颜色", Color) = (0.30, 0.56, 0.46, 1)
        _DeepColor ("深水颜色", Color) = (0.07, 0.25, 0.23, 1)
        _ReflectionColor ("掠射反光颜色", Color) = (0.60, 0.72, 0.66, 1)
        _DepthRange ("达到深水颜色的深度（米）", Range(0.1, 5)) = 1.2
        _Opacity ("深水不透明度", Range(0, 1)) = 0.94
        _ShoreFade ("接岸淡出距离（米）", Range(0.01, 0.5)) = 0.12
        _WaveScale ("波纹密度", Range(0.2, 8)) = 2.5
        _WaveSpeed ("波纹速度", Range(0, 2)) = 0.35
        _WaveStrength ("波纹法线强度", Range(0, 0.3)) = 0.07
        _GlintStrength ("柔和高光强度", Range(0, 1)) = 0.22
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Name "CourtyardWater"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half4 _ReflectionColor;
                float _DepthRange;
                half _Opacity;
                float _ShoreFade;
                float _WaveScale;
                float _WaveSpeed;
                half _WaveStrength;
                half _GlintStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half edge : TEXCOORD1;
                half fog : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = p.positionCS;
                output.positionWS = p.positionWS;
                output.edge = input.color.a;
                output.fog = ComputeFogFactor(p.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float rawDepth = SampleSceneDepth(screenUV);
                // 重建世界坐标兼容透视与正交镜头，取竖直水深避免斜视时颜色变深。
                #if UNITY_REVERSED_Z
                    float deviceDepth = rawDepth;
                    half hasBottom = step(0.00001, rawDepth);
                #else
                    float deviceDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
                    half hasBottom = 1.0h - step(0.99999, rawDepth);
                #endif
                float3 bottomWS = ComputeWorldSpacePosition(screenUV, deviceDepth, UNITY_MATRIX_I_VP);
                float depth = lerp(_DepthRange, max(0.0, input.positionWS.y - bottomWS.y), hasBottom);
                half deep = saturate(depth / max(_DepthRange, 0.001));
                half3 color = lerp(_ShallowColor.rgb, _DeepColor.rgb, deep);

                float2 p = input.positionWS.xz * _WaveScale;
                float t = _Time.y * _WaveSpeed;
                float a = dot(p, float2(0.94, 0.34)) + t;
                float b = dot(p, float2(-0.42, 0.91)) * 1.73 - t * 0.81;
                float c = dot(p, float2(0.57, -0.82)) * 2.31 + t * 0.63;
                float2 slope = cos(a) * float2(0.94, 0.34)
                    + cos(b) * float2(-0.42, 0.91) * 0.55
                    + cos(c) * float2(0.57, -0.82) * 0.28;
                half3 normalWS = normalize(float3(-slope.x * _WaveStrength, 1.0, -slope.y * _WaveStrength));
                half3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);
                // 正交镜头视线平行，使用 URP 的视线函数保持一致。
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light light = GetMainLight(shadowCoord);
                half lit = saturate(dot(normalWS, light.direction));
                half3 illumination = SampleSH(half3(0, 1, 0)) + light.color * lit * light.shadowAttenuation;
                color *= max(illumination, half3(0.35, 0.35, 0.35));
                half fresnel = pow(1.0h - saturate(dot(normalWS, viewDir)), 4.0h);
                color = lerp(color, _ReflectionColor.rgb, fresnel * 0.35h);
                half3 halfDir = SafeNormalize(light.direction + viewDir);
                half glint = pow(saturate(dot(normalWS, halfDir)), 64.0h);
                color += glint * _GlintStrength * light.color * light.shadowAttenuation;
                color += (sin(a) * sin(b) * 0.5h + 0.5h) * _GlintStrength * 0.025h;
                half alpha = lerp(0.45h, _Opacity, deep);
                alpha *= saturate(depth / max(_ShoreFade, 0.001)) * saturate(input.edge);
                return half4(MixFog(color, input.fog), alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
