// 职责：只给 laila 场景中的 Head-topo 提供壁画感表面，不修改 URP 全局 Renderer。
// 新建原因：现有 SpriteDepthClip 服务于 2D 纸片，不能正确表现 SkinnedMeshRenderer 的面部模型。
Shader "21Days/MuralFace"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (0.82, 0.64, 0.48, 1)
        _ShadowColor ("Shadow Color", Color) = (0.34, 0.20, 0.18, 1)
        _ShadowDirection ("Shadow Light Direction", Vector) = (0.35, 0.65, -1, 0)
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.55
        _ShadowThreshold ("Shadow Threshold", Range(0, 1)) = 0.48
        _ShadowSoftness ("Shadow Softness", Range(0.02, 0.5)) = 0.12
        _RimColor ("Rim Color", Color) = (1, 0.76, 0.47, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 3
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.16
        _InkColor ("Ink Color", Color) = (0.035, 0.055, 0.075, 1)
        _InkStrength ("Ink Contour Strength", Range(0, 1)) = 0.24
        _WashColor ("Vermilion Wash", Color) = (0.42, 0.08, 0.055, 1)
        _WashStrength ("Vermilion Wash Strength", Range(0, 0.25)) = 0.035
        _GrainScale ("Grain Scale", Range(1, 80)) = 28
        _GrainStrength ("Grain Strength", Range(0, 0.3)) = 0.08
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            Blend One Zero
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _ShadowColor;
                half4 _ShadowDirection;
                half _ShadowStrength;
                half _ShadowThreshold;
                half _ShadowSoftness;
                half4 _RimColor;
                half _RimPower;
                half _RimStrength;
                half4 _InkColor;
                half _InkStrength;
                half4 _WashColor;
                half _WashStrength;
                half _GrainScale;
                half _GrainStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            half Hash21(float2 value)
            {
                value = frac(value * float2(123.34, 456.21));
                value += dot(value, value + 45.32);
                return frac(value.x * value.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                half3 viewDirection = normalize(GetWorldSpaceViewDir(input.positionWS));
                half3 lightDirection = normalize(_ShadowDirection.xyz);
                half light = saturate(dot(normalWS, lightDirection) * 0.5h + 0.5h);
                half band = smoothstep(
                    _ShadowThreshold - _ShadowSoftness,
                    _ShadowThreshold + _ShadowSoftness,
                    light);
                half shadowAmount = (1.0h - band) * _ShadowStrength;
                half3 muralColor = lerp(
                    _BaseColor.rgb,
                    _BaseColor.rgb * _ShadowColor.rgb,
                    shadowAmount);

                half edge = 1.0h - saturate(dot(normalWS, viewDirection));
                half contour = pow(edge, 2.2h) * _InkStrength;
                muralColor = lerp(muralColor, _InkColor.rgb, contour);

                half rim = pow(edge, _RimPower) * _RimStrength;
                muralColor += _RimColor.rgb * rim;

                half wash = Hash21(input.uv * max(1.0h, _GrainScale * 0.22h));
                muralColor = lerp(
                    muralColor,
                    muralColor * 0.92h + _WashColor.rgb * 0.08h,
                    wash * _WashStrength);

                half grain = Hash21(input.uv * _GrainScale);
                muralColor *= lerp(1.0h - _GrainStrength, 1.0h + _GrainStrength, grain);
                muralColor = MixFog(muralColor, input.fogFactor);

                return half4(saturate(muralColor), 1.0h);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
