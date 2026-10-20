// 职责：真实世界网格的纤维墨雾、破碎墨丝与环带；透明混合保留角色前后深度关系。
// 新建原因：UI 全屏 Shader 的 ZTest Always 不适用，角色 Shader 扩展只会污染衣服着色。
Shader "21Days/Effects/ActorInkWorld"
{
    Properties
    {
        _InkDensityTex ("Optional ink density", 2D) = "gray" {}
        _HasDensity ("Use supplied density", Float) = 0
        [PerRendererData] _LayerProgress ("Local bloom age", Range(0,1)) = 0
        [PerRendererData] _LayerOpacity ("Layer opacity", Range(0,1)) = 0
        [PerRendererData] _ShapeKind ("Mist 0, ribbon 1, foreground stroke 2", Float) = 0
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "IgnoreProjector"="True"
        }
        Pass
        {
            Name "ActorInkWorld"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D(_InkDensityTex);
            SAMPLER(sampler_InkDensityTex);
            CBUFFER_START(UnityPerMaterial)
                float _HasDensity;
                float _LayerProgress;
                float _LayerOpacity;
                float _ShapeKind;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 seed : TEXCOORD1;
                half4 color : COLOR;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 seed : TEXCOORD1;
                half opacity : TEXCOORD2;
                half coreDensity : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                // 不写入屏幕位置、不重写深度；实体与 alpha-clip 纸片都使用实际世界深度遮挡墨层。
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.seed = input.seed;
                output.opacity = input.color.a;
                output.coreDensity = input.color.r;
                return output;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(cell), Hash(cell + float2(1, 0)), f.x),
                    lerp(Hash(cell + float2(0, 1)), Hash(cell + 1.0), f.x), f.y);
            }

            float Wash(float2 p)
            {
                return Noise(p) * 0.57 + Noise(p * 2.07 + 11.3) * 0.29
                    + Noise(p * 4.31 + 7.8) * 0.14;
            }

            float Density(float2 uv)
            {
                // 没有外部贴图仍有完整的程序化纤维；贴图只轻微调节浓淡，不直接显示灰云图片。
                float grain = 1.0 - SAMPLE_TEXTURE2D(_InkDensityTex, sampler_InkDensityTex, frac(uv)).r;
                return lerp(0.5, grain, saturate(_HasDensity));
            }

            half Mist(Varyings input)
            {
                float2 q = (input.uv - 0.5) * 2.0;
                float2 seed = input.seed * float2(39.7, 53.1);
                float2 flow = float2(_LayerProgress * 0.16, -_LayerProgress * 0.23);
                float2 domain = q * 2.4 + seed + flow;
                float2 warp = float2(Wash(domain + 3.2), Wash(domain + 15.6)) - 0.5;
                float2 warped = q + warp * 0.48;
                float cloud = Wash(domain + warp * 2.1);
                float fiber = Noise(warped * float2(23.0, 71.0) + seed);
                float grain = Density(warped * 0.44 + seed * 0.17);
                // 轮廓受多尺度渗边扰动；内外均有空隙，避免“圆形透明黑烟贴片”。
                float contour = 0.78 - length(warped) + (cloud - 0.5) * 0.62 + (fiber - 0.5) * 0.065;
                float edge = smoothstep(-0.1, 0.27, contour);
                float ragged = smoothstep(0.31, 0.56, cloud + warp.x * 0.2);
                // 留白按较大的块面切开；细噪只啃边，不能把整个主体乘成均匀灰烟。
                float dryGaps = smoothstep(0.34, 0.53, Noise(domain * 1.35 + warp * 3.0));
                float ridge = 1.0 - smoothstep(0.055, 0.18, abs(cloud - 0.51 + warp.y * 0.12));
                float filaments = smoothstep(0.34, 0.79, fiber + cloud * 0.24);
                float core = smoothstep(0.04, 0.28, contour);
                float pigment = max(ragged * 0.78, ridge) * dryGaps
                    * lerp(0.35 + filaments * 0.65, 1.0, core);
                float haze = edge * (1.0 - core) * (0.012 + cloud * 0.026) * dryGaps;
                float ink = edge * pigment * (0.82 + grain * 0.16);
                // 四边确实归零，任何噪声也不能暴露四边形边框。
                float2 margins = min(input.uv, 1.0 - input.uv);
                float quadFeather = smoothstep(0.0, 0.095, min(margins.x, margins.y));
                return saturate((haze + ink) * quadFeather);
            }

            half Ribbon(Varyings input)
            {
                float angle = input.uv.x * TWO_PI;
                // 周向采样首尾连续；细碎断口分布不随进度随机跳动。
                float2 circle = float2(cos(angle), sin(angle));
                float2 domain = circle * 4.7 + float2(13.7, 6.2);
                float cloud = Wash(domain);
                float across = input.uv.y * 2.0 - 1.0;
                float warpedAcross = across + (Noise(domain * 3.1) - 0.5) * 0.44;
                float fiber = Noise(circle * 55.0 + input.uv.y * float2(19.0, 23.0) + cloud * 4.0);
                float edge = 1.0 - smoothstep(0.35, 0.96, abs(warpedAcross) + (fiber - 0.5) * 0.15);
                float breaks = smoothstep(0.32, 0.58, cloud);
                float threads = smoothstep(0.28, 0.73, fiber + cloud * 0.16);
                // 连贯的浓墨脊贯穿每段断环，纤维只在旁侧干裂，避免整圈都是半透明毛线。
                float spine = 1.0 - smoothstep(0.12, 0.50, abs(warpedAcross + (cloud - 0.5) * 0.15));
                float grain = Density(circle * 0.35 + input.uv.y * 0.13);
                float feather = smoothstep(0.0, 0.13, input.uv.y) * (1.0 - smoothstep(0.87, 1.0, input.uv.y));
                float pigment = 0.15 + max(spine * 0.88, threads * 0.7) * 0.85;
                return saturate(edge * feather * breaks * pigment * (0.85 + grain * 0.15));
            }

            half2 ForegroundStroke(Varyings input)
            {
                // 弧状宽墨脊沿浅 U 网格包围衣摆，窄处仍有墨芯，缺口只从边侧咬入。
                float2 q = (input.uv - 0.5) * 2.0;
                float seed = input.seed.x * 17.2 + input.seed.y * 6.3;
                float broad = Noise(float2(q.x * 2.8 + seed, seed * 0.7 + 3.1));
                float centerNoise = Noise(float2(seed, seed * 0.7 + 3.1));
                float middle = q.x * q.x * 0.46 - 0.10 + clamp((broad - centerNoise) * 0.26
                    + (sin(q.x * 3.5 + seed) - sin(seed)) * 0.06, -0.18, 0.18);
                float thickness = 0.30 + Noise(float2(q.x * 4.3 + seed, seed + 9.7)) * 0.28;
                float distanceToSpine = abs(q.y - middle);
                float fiber = Noise(float2(q.x * 43.0 + seed, q.y * 91.0 + seed));
                float dryBite = smoothstep(0.59, 0.82, Noise(float2(q.x * 8.1 + seed, seed + 5.4)))
                    * smoothstep(0.08, 0.38, distanceToSpine) * 0.24;
                float raggedDistance = distanceToSpine + (fiber - 0.5) * 0.13 + dryBite;
                float ridge = 1.0 - smoothstep(thickness * 0.45, thickness, raggedDistance);
                float fringe = (1.0 - smoothstep(thickness, thickness + 0.16, raggedDistance))
                    * (1.0 - ridge) * 0.12;
                // 端部略留断笔，重叠网格也不会形成一圈等宽实心腰带。
                float tips = smoothstep(0.0, 0.12, input.uv.x)
                    * (1.0 - smoothstep(0.86, 1.0, input.uv.x));
                float sideFeather = smoothstep(0.0, 0.12, input.uv.y)
                    * (1.0 - smoothstep(0.88, 1.0, input.uv.y));
                float grain = Density(q * 0.31 + input.seed);
                float alpha = saturate((ridge * (0.9 + grain * 0.1) + fringe) * tips * sideFeather);
                // 增密从墨芯向原有边缘内侧平滑归零；不扩大轮廓，也不抬高稀薄的渗边。
                float core = (1.0 - smoothstep(thickness * 0.22, thickness * 0.72, raggedDistance))
                    * tips * sideFeather;
                return half2(alpha, core);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half opacity;
                UNITY_BRANCH
                if (_ShapeKind > 1.5)
                {
                    half2 stroke = ForegroundStroke(input);
                    opacity = stroke.x * saturate(_LayerOpacity) * input.opacity;
                    // 仅前片墨芯增加吸收；基础透明度为零时仍为零，两侧比身体中央更浓。
                    float density = saturate(input.coreDensity) * stroke.y;
                    if (density > 0.0 && opacity > 0.0 && opacity < 1.0)
                        opacity = 1.0 - pow(1.0 - opacity, 1.0 + density);
                }
                else
                {
                    opacity = _ShapeKind > 0.5 ? Ribbon(input) : Mist(input);
                    opacity *= saturate(_LayerOpacity) * input.opacity;
                }
                // 不 AlphaClip 墨边，也不写深度：纤维透明羽化应保留。角色本身仍按原材质写深度。
                half3 ink = half3(0.015, 0.020, 0.020);
                #ifndef UNITY_COLORSPACE_GAMMA
                ink = SRGBToLinear(ink);
                #endif
                return half4(ink, opacity);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
