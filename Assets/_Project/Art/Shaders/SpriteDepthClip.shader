// 职责：角色 2D 纸片在 3D 透视场景（Universal Renderer）里的着色——Alpha Test 写深度、受雾、投影与参与 SSAO。
// 为什么不复用：URP 自带 Sprite-Lit/Unlit-Default 走透明队列、不写深度，也没有 ShadowCaster / DepthNormals Pass，
//   纸片在 SSAO 与阴影里等于不存在，与 3D 灰盒互相穿插时排序也不稳；URP/Lit 的 Alpha Clipping 能写深度，
//   但不吃 SpriteRenderer 的顶点色（状态色要靠它），且会给纸片加上不想要的 PBR 高光。
// 为什么不扩展：工程里此前没有任何 .shader 可扩展；模板自带着色器在 Packages 里，不能改。
// 本着色器只管表现，不含任何玩法逻辑。
// 注意：受雾、投影、参与 SSAO，但不接收场景阴影与光照（UniversalForward 只做裁剪 + 顶点色 + 雾，没有 NdotL）——
//   这是有意取舍：纸片角色靠脚下 BlobShadow 落地，避免 3D 光照把手绘明暗打乱。
//
// 深度偏移 _DepthBias（2026-09-30 加，修「玩家与 NPC 两张纸片交错叠加」）：
//   · 起因：纸片都经 CameraBillboard 转成与相机成像平面平行，站在同一排（同 z、同地面高度）的两个角色观察深度完全相同
//     （实测出生点那一排 z 3.4 的三个 NPC 小人都是 16.5870），深度缓冲分不出前后，重叠处两张图的像素交错（z-fighting）。
//   · 做法：深度改成「沿观察方向往相机挪 _DepthBias 个世界单位」那一点的深度，裁剪空间 xy 与 w 不动，画面位置一点不变；
//     只有玩家用带偏移的材质（M_SpriteDepthClip_Player），其它角色保持 0。于是深度打平时玩家稳定画在前面，
//     真实深度差大于偏移量时仍按真实深度遮挡，与灰盒的遮挡关系不变（仍是 Alpha Test + ZWrite，不进透明队列）。
//   · 取值 0.02（写在 M_SpriteDepthClip_Player.mat），按探索相机实际参数估算（SampleScene Main Camera：
//     透视 FOV 20、俯角 38°、跟随偏移 (0, 11.81, −14)、Near 0.5、Far 100）：
//       角色观察深度 d：脚底 d0 = 11.81·sin38° + 14·cos38° ≈ 7.27 + 11.03 ≈ 18.3 m；画面上下沿射线打到地面分别是
//         11.81 / sin(38° ± 10°) · cos10° ≈ 15.6 m 与 24.8 m，所以画面内的角色 d ∈ [15.6, 24.8]，取上界 25。
//       深度精度（保守按 24 位定点深度估算；桌面 URP 多为 32 位浮点反向 Z，精度更高）：
//         透视深度 z_ndc ≈ n / d，一个最小刻度 2^-24 ≈ 6.0e-8 对应的世界深度 Δd = d² / n · 2^-24 = 25² / 0.5 · 6.0e-8 ≈ 7.5e-5 m。
//       约束：偏移 b ≫ Δd（打平时稳定分出前后，扛得住光栅化插值的几个刻度误差），且 b 小于「明显深度差」约 0.05 m。
//       取 b = 0.02：在画面最远处仍约为 270 个深度刻度，又给 0.05 留出 0.03 的余量。
//   · 为什么不用 ShaderLab 的 Offset：它按「深度缓冲最小刻度」计，同一个数值换成世界单位随 d² 变、随深度格式变，
//     近处够用远处就过量；这里直接按世界单位偏移，远近一致。
//   · 每个在相机视角下写深度 / 做深度测试的 Pass（UniversalForward、DepthOnly、DepthNormals）都用同一个 ApplyDepthBias，
//     深度预通道（含 URP 深度预写 + ZTest Equal 的情况）和前向通道对得上。ShadowCaster 不加：阴影图从光源方向渲染，
//     这条偏移管的是「相机视角下纸片之间谁在前」，加进去只会让影子挪位；何况 SpriteRenderer 默认不投影。
Shader "21Days/SpriteDepthClip"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        // 深度偏移（世界单位，朝相机）；只给玩家的材质变体设正值，取值依据见文件头。
        _DepthBias ("Depth Bias (world units toward camera)", Range(0, 0.05)) = 0
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

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);

        // SRP Batcher：材质属性全部放进 UnityPerMaterial，且每个 Pass 布局一致。
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _Color;
            half _Cutoff;
            float _DepthBias;
        CBUFFER_END

        half SampleAlpha(float2 uv, half vertexAlpha)
        {
            return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a * _Color.a * vertexAlpha;
        }

        // 深度偏移：把裁剪空间 z 换成「观察空间里往相机挪 _DepthBias 个世界单位」那一点的深度，xy 与 w 原样不动。
        // 走同一个投影矩阵求偏移点的 NDC 深度，DX 反向 Z / OpenGL 正向 Z / 正交相机都不用另分支；
        // 只有「别越过近裁剪面」这一步要按 UNITY_REVERSED_Z 区分方向。_DepthBias 为 0 时原样返回，其它角色的深度与改前逐位一致。
        // （不写提前 return：分支里提前返回会让着色器编译器报「可能未初始化」警告。）
        float4 ApplyDepthBias(float3 positionWS, float4 positionCS)
        {
            UNITY_BRANCH
            if (_DepthBias != 0.0)
            {
                float3 positionVS = TransformWorldToView(positionWS);
                // Unity 观察空间是右手系、相机看向 -Z：z 加正数 = 离相机更近。
                positionVS.z += _DepthBias;
                float4 biasedCS = TransformWViewToHClip(positionVS);
                // 光栅化仍按原 w 做透视除法，所以把偏移点的 NDC 深度乘回原 w。
                float z = biasedCS.z / biasedCS.w * positionCS.w;
                // 偏移后不越过近裁剪面；原本就在近裁剪面外的顶点保持原来的裁剪结果。
            #if UNITY_REVERSED_Z
                z = min(z, max(positionCS.z, UNITY_NEAR_CLIP_VALUE * positionCS.w));
            #else
                z = max(z, min(positionCS.z, UNITY_NEAR_CLIP_VALUE * positionCS.w));
            #endif
                positionCS.z = z;
            }
            return positionCS;
        }
        ENDHLSL

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
            #pragma multi_compile_fog

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
                half4 color : COLOR;
                half fogFactor : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = ApplyDepthBias(positionInputs.positionWS, positionInputs.positionCS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color * _Color;
                // 雾按真实深度算，偏移只影响深度测试。
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color;
                clip(color.a - _Cutoff);
                color.rgb = MixFog(color.rgb, input.fogFactor);
                return half4(color.rgb, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            Cull Off
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

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
                float2 uv : TEXCOORD0;
                half alpha : TEXCOORD1;
            };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                output.positionCS = positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.alpha = input.color.a;
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                clip(SampleAlpha(input.uv, input.alpha) - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Cull Off
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

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

            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                // 与 UniversalForward 同一算法（GetVertexPositionInputs 也是先到世界再到裁剪空间），深度预通道与前向通道逐位一致。
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = ApplyDepthBias(positionWS, TransformWorldToHClip(positionWS));
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.alpha = input.color.a;
                return output;
            }

            half DepthFrag(Varyings input) : SV_Target
            {
                clip(SampleAlpha(input.uv, input.alpha) - _Cutoff);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag

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
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half alpha : TEXCOORD2;
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = ApplyDepthBias(positionWS, TransformWorldToHClip(positionWS));
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.alpha = input.color.a;
                return output;
            }

            half4 DepthNormalsFrag(Varyings input, half facing : VFACE) : SV_Target
            {
                clip(SampleAlpha(input.uv, input.alpha) - _Cutoff);
                // 双面纸片：背面翻转法线，避免 SSAO 读到朝里的法线。
                float3 normalWS = normalize(input.normalWS) * (facing > 0 ? 1.0 : -1.0);
                return half4(NormalizeNormalPerPixel(normalWS), 0.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
