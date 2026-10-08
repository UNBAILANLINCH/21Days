// 职责：战斗受击「闪白」——把序列帧纸片按自身 alpha 裁剪后整片涂成纯色（默认白），表现层在受击瞬间临时换上这份材质。
// 为什么不复用：SpriteDepthClip 输出 贴图色 × 顶点色，SpriteRenderer 的顶点色被钳在 0..1，乘不出比原图更亮的白；
//   URP 自带 Sprite-Unlit/Lit-Default 同理只会乘色。
// 为什么不扩展 SpriteDepthClip：它被探索里所有角色共用，且 EditMode 守卫（SpriteDepthBiasWiringTests）钉住了两份材质只差 _DepthBias；
//   加闪白属性会改 UnityPerMaterial 布局、牵动全部角色材质。闪白只在战斗里用几十毫秒，单独一份最省事。
// 只有 UniversalForward 一个 Pass：换上这份材质的几十毫秒里纸片不进深度预通道 / SSAO（工程未开 Depth Priming，见 UniversalRenderer.asset），
//   肉眼看不出；Alpha Test + ZWrite 与 SpriteDepthClip 一致，换材质前后的前后遮挡不变。不受雾：闪白要的是纯色。
Shader "21Days/SpriteFlash"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _FlashColor ("Flash Color", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "IgnoreProjector" = "True"
            "CanUseSpriteAtlas" = "True"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _FlashColor;
                half _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half alpha : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.alpha = input.color.a;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a * input.alpha;
                clip(alpha - _Cutoff);
                return half4(_FlashColor.rgb, 1.0h);
            }
            ENDHLSL
        }
    }
}
