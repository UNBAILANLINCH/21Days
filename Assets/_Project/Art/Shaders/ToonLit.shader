// 职责：3D 环境（灰盒 + 正式低模）的着色——「手绘贴图 + 轻明暗」，让 3D 环境和 2D 纸片角色落在同一套画法里。
// 为什么新建（project-root.md「加能力的顺序」）：
//   1. 复用不行：工程里现有的 4 个着色器（SpriteDepthClip / DarkFog / MuralFace / SpriteFlash）都只画 2D 纸片或全屏效果，
//      没有一个管 3D 网格的受光；模板自带的 URP/Lit 在 Packages 里，不能改。
//   2. 扩展不行：往上面任何一个里塞 3D 受光，名实不符，且那些着色器不进 ShadowCaster/DepthNormals 的 3D 路径。
//
// 解决的问题：场景此前是 URP/Lit 直出——PBR 的连续明暗、平滑高光，和手绘 Q 版纸片角色放在一起，
//   一个「写实」一个「手绘」，画面对不上。参考风格（美术手册 3.1：明日方舟探索地图）是「低模 + 手绘贴图 + Q 版纸片」：
//   颜色和体积**主要来自贴图本身**，着色器只负责一层很轻的明暗，不是硬边平涂、也不是 PBR。
//
// 画法（别改回硬边平涂 / 全场描边，那是插画剪影的画法，docs/artist-guide.md 3.1.1）：
//   · 轻明暗：亮部 = 贴图原色 × 光色；暗部只比亮部暗一点、冷一点（_ShadowColor 很浅）。
//     明暗交界用 _RampSmooth 给一段柔和过渡，不是一刀切的分层。
//   · 基色下限（_AlbedoFloor）：按贴图**局部平均亮度**算增益、rgb 等比放大，深色贴图被提亮，
//     但色相不变（深绿还是绿），贴图里的手绘明暗层次也不被抹平。
//   · 剔除（_Cull）：默认 Back；单面开放的植物卡片（草卡、叶片卡、蕨卡）设 Off，背面按 VFACE 翻法线，正反两面受光一致。
//     转换器（Editor/Art/ToonMaterialConverter.cs）按材质名给植物设 Off。
//   · 临时调色（_GradeHueShift / _GradeSaturation / _GradeValue）：美术按参考配色重出贴图之前的过渡方案，
//     在基色下限之后、受光之前把贴图颜色往暖金、赭石拧。中性值时整段跳过；新贴图到了就归零。
//     权威值在 ToonMaterialMap.asset 的调色表里，由转换器写进材质（理由见 ToonMaterialMap.cs 文件头）。
//   · 高光 / 边缘光：保留开关，默认 0。参考图的环境没有这两样，加了会浮出塑料感。
//   · 不描边：参考图的环境没有轮廓线。将来「可交互物高亮」是逐物体、动态开关的效果，
//     同一份材质上的反壳描边做不到（一开就是这份材质的所有物体一起亮），到时另做，不放在这份着色器里。
//
// 阴影接收：主光实时阴影走 URP 的 GetMainLight(shadowCoord)，没进阴影的部分一并算进暗部（乘 _ShadowColor），
//   于是「物体被别的物体挡住」和「背光」看起来是同一种暗部色。
//
// 光照一致性：附加光（点光 / 聚光）在同一个前向 Pass 里循环，用同一套过渡，不引入第二套明暗逻辑。
//   雾、阴影投射（ShadowCaster）、深度（DepthOnly）、SSAO 用的法线（DepthNormals）四样齐备，
//   少了任何一个，环境物体在雾里、阴影里或后期 AO 里就会「消失」或与其它物体表现不一致。
//   四个几何 Pass 都用同一个 Cull [_Cull]：剔除不一致的话，双面卡片会出现「看得见却不投影 / 不写深度」。
//
// 参数命名沿用 URP/Lit 的 _BaseMap / _BaseColor / _Cutoff，是为了让「从 URP/Lit 换过来」时贴图与颜色能直接搬，
//   不用每个材质重配；SRP Batcher 需要的 UnityPerMaterial 在各 Pass 里布局一致。
Shader "21Days/ToonLit"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)

        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha Clip", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5

        // 剔除模式。默认 Back（闭合网格）。单面开放的植物卡片必须 Off：
        // Back 时背对相机的那一半卡片直接不画，植物稀掉一半；双面时背面法线在片元里翻过来，受光与正面一致。
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull（植物卡片用 Off）", Float) = 2

        // 暗部色：冷偏紫，但**整体压得很浅**。
        // 参考风格（明日方舟探索地图）的环境是「手绘贴图 + 轻度明暗」，不是硬边双层平涂：
        // 暗部只比亮部冷一点、暗一点，靠贴图本身的手绘明暗出体积。压深了会把贴图细节吃掉。
        _ShadowColor ("Shadow Color（暗部色，往冷色偏）", Color) = (0.80, 0.83, 0.92, 1)
        _ShadowThreshold ("Shadow Threshold（明暗分界，0 = 正面偏亮）", Range(-1, 1)) = 0
        _RampSmooth ("Ramp Smooth（过渡宽度，0.08 左右接近手绘的柔和过渡）", Range(0.001, 0.5)) = 0.08
        // 环境光进入暗部的比例。场景环境光是 Flat 灰色，全放进来会把明暗差抹平，所以压得很低。
        _AmbientStrength ("Ambient Strength（环境光进入暗部的比例）", Range(0, 1)) = 0.15

        // 基色下限（线性空间亮度，0 = 关）：深色贴图再乘一次明暗容易糊成一团，这里把暗贴图整体提亮。
        // **按贴图局部平均亮度算增益、rgb 三通道乘同一个增益**，所以色相、饱和度和贴图内部的明暗层次都不变——
        // 不能逐通道取 max：那样深绿 (0.003, 0.146, 0.111) 会被抬成 (0.22, 0.22, 0.22) 的灰，叶子发灰就是这么来的。
        // 增益封顶 8 倍（ALBEDO_FLOOR_MAX_GAIN），极暗贴图里的压缩噪点不会被放大成花斑。细节见 ApplyAlbedoFloor。
        _AlbedoFloor ("Albedo Floor（基色亮度下限，深色贴图用）", Range(0, 1)) = 0.12

        // 关掉实时阴影接收。薄叶片材质必须关：树冠是一片片单面叶片卡，
        // 开着时每张卡都给后面的卡投一层自阴影，整棵树会压成近黑。
        [Toggle(_IGNORE_SHADOWS_ON)] _IgnoreShadows ("Ignore Realtime Shadows（叶片用）", Float) = 0

        // 暗部下限。「背光面」不该是纯黑：叶片是单面片，背光那侧会被压到最暗，给一个下限保住形体。
        _LitMin ("Lit Min（暗部最低受光量）", Range(0, 1)) = 0.35

        // 半兰伯特：把 NdotL 从 [-1,1] 重映射到 [0,1] 再过渡。叶片卡法线杂乱，开着才不死黑。
        [Toggle(_HALF_LAMBERT_ON)] _HalfLambert ("Half Lambert（叶片用）", Float) = 0

        // 高光与边缘光**默认关掉**。参考风格里环境物体没有这两样：
        // 环境是手绘贴图本身带明暗，不是靠着色器加油光；加了会让灰盒/低模浮出一层塑料感。
        _SpecColor2 ("Specular Color", Color) = (1, 1, 1, 1)
        _SpecThreshold ("Specular Threshold", Range(0, 1)) = 0.9
        _SpecSmooth ("Specular Smooth", Range(0.001, 0.5)) = 0.02
        _SpecIntensity ("Specular Intensity", Range(0, 2)) = 0

        _RimColor ("Rim Color", Color) = (1, 1, 1, 1)
        _RimPower ("Rim Power（越大越贴边）", Range(0.5, 16)) = 6
        _RimIntensity ("Rim Intensity", Range(0, 2)) = 0

        // ---- 临时调色旋钮：美术新贴图到位前的过渡方案 ----
        // env_well 的贴图是青绿 / 翠绿，参考图是暖金、赭石；美术按参考配色重出贴图之前，先用这三个旋钮把颜色拧过去。
        // **新贴图到了就把旋钮归零**（色相 0、饱和度 1、明度 1），不要在新贴图上继续叠。
        // 作用在基色上（基色下限之后、受光之前），在 sRGB 空间里做，度数与 Photoshop「色相/饱和度」的直觉一致。
        // 中性值（0 / 1 / 1）时着色器整段跳过，画面与没有旋钮时逐像素一致。
        // 权威值不在材质上，在 Art/Materials/Toon/ToonMaterialMap.asset 的调色表里，转换器每次重写材质时从表里写回；
        // 直接拖材质上的滑块，下次转换会被表里的值覆盖——拖好后跑菜单「三渲二：材质上的调色旋钮存回对应表」。
        _GradeHueShift ("Grade Hue Shift（临时：色相偏移，度）", Range(-180, 180)) = 0
        _GradeSaturation ("Grade Saturation（临时：饱和度倍数）", Range(0, 2)) = 1
        _GradeValue ("Grade Value（临时：明度倍数）", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        // SRP Batcher：材质属性全部放进 UnityPerMaterial，且每个 Pass 布局一致。
        // _Cull 只用在渲染状态（Cull [_Cull]）上，着色代码不读它，所以不进这块。
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Cutoff;
            half4 _ShadowColor;
            half _ShadowThreshold;
            half _RampSmooth;
            half _AmbientStrength;
            half _IgnoreShadows;
            half _LitMin;
            half _HalfLambert;
            half _AlbedoFloor;
            half4 _SpecColor2;
            half _SpecThreshold;
            half _SpecSmooth;
            half _SpecIntensity;
            half4 _RimColor;
            half _RimPower;
            half _RimIntensity;
            float _GradeHueShift;
            half _GradeSaturation;
            half _GradeValue;
        CBUFFER_END

        // 基色下限的增益上限。贴图越暗、需要的增益越大；不封顶的话近黑贴图里的压缩噪点会被放大成花斑。
        #define ALBEDO_FLOOR_MAX_GAIN 8.0h

        // 算「局部平均亮度」用的 mip 层。第 6 层的一个 texel ≈ 原图 64×64 像素块的平均（2048 图是 32×32 格）。
        // 层数够高才平均得掉笔触，又不至于高到整张图只剩一个均值（图集里不同元素亮度不同，要各算各的）。
        #define ALBEDO_FLOOR_MIP 6.0

        // 基色下限：按**局部平均亮度**算一个增益，rgb 三通道乘同一个增益——色相、饱和度不变，贴图内部的明暗层次也不变。
        //   · 为什么不逐像素算增益：实测树冠贴图逐像素亮度 0.07～0.16（5%～95% 分位）全在植物下限 0.22 以下，
        //     逐像素抬到下限 = 每个像素亮度都变成 0.22，叶片的手绘层次被抹成一块平涂青色。
        //     按局部平均算，整块贴图乘同一个倍数，亮的叶子仍比暗的叶子亮。
        //   · 抠图贴图的透明区 rgb 是黑的，mip 平均会被它拉暗；除以平均 alpha 还原成「不透明部分的平均色」。
        //   · 贴图没有 mip 时 LOD 采样退回第 0 层，等于逐像素增益——仍保色相，只是不保层次。
        //   · 增益 = 下限 / 局部亮度，夹在 [1, 8]：局部已达标的区域增益是 1，原样不动。
        half3 ApplyAlbedoFloor(half3 albedo, float2 uv)
        {
            half4 localAverage = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, uv, ALBEDO_FLOOR_MIP) * _BaseColor;
            half localLuminance = Luminance(localAverage.rgb) / max(localAverage.a, 0.05h);
            half gain = clamp(_AlbedoFloor / max(localLuminance, HALF_MIN), 1.0h, ALBEDO_FLOOR_MAX_GAIN);
            return albedo * gain;
        }

        // 临时调色（美术新贴图到位前的过渡方案，见 Properties 里旋钮的注释）：色相偏移 → 饱和度 → 明度。
        //   · 在 sRGB 空间里做：旋钮的度数、倍数和美术在 Photoshop 里对贴图调「色相/饱和度」时的直觉一致，
        //     调色目标也是从参考图上按 sRGB 取的色。
        //   · 三个旋钮都在中性值时整段跳过（材质级常量，分支对整批像素一致，不产生发散）：
        //     不跳的话 sRGB↔线性、RGB↔HSV 两次往返有浮点误差，中性时就做不到与原画面逐像素一致。
        half3 ApplyGrade(half3 albedo)
        {
            UNITY_BRANCH
            if (_GradeHueShift != 0.0 || _GradeSaturation != 1.0h || _GradeValue != 1.0h)
            {
                half3 hsv = RgbToHsv(LinearToSRGB(albedo));
                hsv.x = frac(hsv.x + _GradeHueShift / 360.0);
                hsv.y = saturate(hsv.y * _GradeSaturation);
                hsv.z = hsv.z * _GradeValue;
                albedo = SRGBToLinear(HsvToRgb(hsv));
            }
            return albedo;
        }

        // 明暗过渡：把 NdotL 压成亮 / 暗两侧，过渡宽度由 _RampSmooth 控制（默认 0.08，柔和过渡）。
        // _HALF_LAMBERT_ON 时先把 NdotL 重映射成 [0,1]（半兰伯特），叶片专用，理由见 Properties 里的注释。
        half ToonRamp(half ndotl)
        {
        #ifdef _HALF_LAMBERT_ON
            ndotl = ndotl * 0.5h + 0.5h;
        #endif
            return smoothstep(_ShadowThreshold - _RampSmooth, _ShadowThreshold + _RampSmooth, ndotl);
        }

        // 主光的「受光量」：几何项 × 实时阴影 × 距离衰减，任何一项不足都算进暗部。
        // 距离衰减对平行光是 1，留着是为了同一个函数能给附加光复用。
        // _LitMin 给一个下限：叶片这类单面片，背光那侧会被压到最暗，不设下限整棵树会糊成黑块。
        half LightAttenuation(Light light, half ndotl)
        {
            half lit = ToonRamp(ndotl);
        #ifdef _IGNORE_SHADOWS_ON
            // 叶片：不接收实时阴影，明暗只看法线。理由见 Properties 里 _IgnoreShadows 的注释。
            return max(lit, _LitMin);
        #else
            return max(lit * light.shadowAttenuation * light.distanceAttenuation, _LitMin);
        #endif
        }

        // 一层光的漫反射贡献：亮部用光色，暗部换成 _ShadowColor。
        //   · 亮部乘 light.color（不是纯白）：保留光的色温，暖光下的亮部才不发青；「受光 = 贴图原色」。
        //   · 暗部只乘 _ShadowColor 加一点环境光：暗部要偏冷，而且不能被环境光稀释——
        //     环境光的比例由 _AmbientStrength 控制，默认很低。
        half3 ToonDiffuse(Light light, half ndotl, half3 ambient)
        {
            half lit = LightAttenuation(light, ndotl);
            half3 litColor = light.color;
            half3 shadowLitColor = _ShadowColor.rgb * (ambient + light.color * 0.10h);
            return lerp(shadowLitColor, litColor, lit);
        }

        // 硬边高光：Blinn-Phong 过阈值。_SpecIntensity 为 0 时直接跳过。
        // 单出口写法：分支里提前 return 会让编译器报「potentially uninitialized variable」。
        half3 ToonSpecular(half3 normalWS, half3 viewDirWS, Light light)
        {
            half3 specular = half3(0.0h, 0.0h, 0.0h);
            UNITY_BRANCH
            if (_SpecIntensity > 0.0h)
            {
                half3 halfDirWS = normalize(light.direction + viewDirWS);
                half spec = dot(normalWS, halfDirWS);
                half band = smoothstep(_SpecThreshold - _SpecSmooth, _SpecThreshold + _SpecSmooth, spec);
                specular = _SpecColor2.rgb * _SpecIntensity * band * light.shadowAttenuation;
            }
            return specular;
        }

        // 边缘光：视线越掠射越亮，在轮廓内侧给一圈亮边。默认强度 0。
        half3 ToonRim(half3 normalWS, half3 viewDirWS)
        {
            half rim = pow(saturate(1.0h - saturate(dot(normalWS, viewDirWS))), _RimPower);
            return _RimColor.rgb * (_RimIntensity * rim);
        }

        ENDHLSL

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _IGNORE_SHADOWS_ON
            #pragma shader_feature_local _HALF_LAMBERT_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                half4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half4 color = baseMap * _BaseColor;

            #ifdef _ALPHATEST_ON
                clip(color.a - _Cutoff);
            #endif

                color.rgb = ApplyAlbedoFloor(color.rgb, input.uv);
                color.rgb = ApplyGrade(color.rgb);

                // 双面卡片（_Cull = Off）的背面法线朝里，直接拿去算受光会把整张卡判成背光。
                // 按正反面翻过来，正反两面用同一个朝向受光。Cull Back 时只有正面进来，这一步等于不做。
                float3 normalWS = normalize(input.normalWS) * IS_FRONT_VFACE(facing, 1.0, -1.0);
                float3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));

                // 环境光：只在暗部用得上，所以先按 _AmbientStrength 压掉——它只是「别死黑」的兜底，
                // 不该像 PBR 那样给整幅画面托底，否则明暗会被抹平。
                half3 ambient = SampleSH(normalWS) * _AmbientStrength;

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half3 lighting = ToonDiffuse(mainLight, dot(normalWS, mainLight.direction), ambient);

            #if defined(_ADDITIONAL_LIGHTS)
                uint additionalCount = GetAdditionalLightsCount();
                for (uint lightIndex = 0u; lightIndex < additionalCount; ++lightIndex)
                {
                    Light light = GetAdditionalLight(lightIndex, input.positionWS);
                    // 附加光叠加时按「取更亮的一层」而不是线性相加，避免多灯处出现亮部叠爆。
                    half3 contribution = ToonDiffuse(light, dot(normalWS, light.direction), ambient);
                    lighting = max(lighting, contribution);
                }
            #endif

                half3 finalColor = color.rgb * lighting;
                finalColor += ToonSpecular(normalWS, viewDirWS, mainLight);
                // 边缘光**不乘基色**：它是轮廓内侧独立的一道亮边，乘了基色的话深色物体等于没有边缘光。
                finalColor += ToonRim(normalWS, viewDirWS);
                finalColor = MixFog(finalColor, input.fogFactor);

            #ifdef _ALPHATEST_ON
                return half4(finalColor, 1.0h);
            #else
                return half4(finalColor, color.a);
            #endif
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma shader_feature_local _ALPHATEST_ON
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
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output = (Varyings)0;
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
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
            #ifdef _ALPHATEST_ON
                half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a;
                clip(alpha - _Cutoff);
            #endif
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma shader_feature_local _ALPHATEST_ON

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half DepthFrag(Varyings input) : SV_Target
            {
            #ifdef _ALPHATEST_ON
                half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a;
                clip(alpha - _Cutoff);
            #endif
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma shader_feature_local _ALPHATEST_ON

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFrag(Varyings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
            #ifdef _ALPHATEST_ON
                half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a;
                clip(alpha - _Cutoff);
            #endif
                // 与前向 Pass 同一个翻法：双面卡片的背面写进法线缓冲的也必须是朝向相机的那一侧，
                // 否则 SSAO 会把卡片背面当成凹陷处，压出一片脏黑。
                float3 normalWS = input.normalWS * IS_FRONT_VFACE(facing, 1.0, -1.0);
                return half4(NormalizeNormalPerPixel(normalWS), 0.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
