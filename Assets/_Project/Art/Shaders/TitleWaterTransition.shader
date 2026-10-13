// 职责：标题截图的同心水波、渐强模糊、稀疏径向速度线和末拍暗幕。
// 新建原因：现有 Shader 处理场景物体，没有可直接复用的全屏 UI 扭曲；无需新增 URP Renderer Feature。
Shader "21Days/UI/TitleWaterTransition"
{
    Properties
    {
        [PerRendererData] _MainTex ("Title capture", 2D) = "white" {}
        _Progress ("Progress", Range(0,1)) = 0
        _Center ("Center / aspect", Vector) = (0.5,0.5,1.7778,0)
        _Strength ("Wave / blur / lines", Vector) = (0.024,0.018,0.3,0)
        _FlipY ("Capture vertical orientation", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            sampler2D _MainTex;
            float _Progress;
            float _FlipY;
            float4 _Center, _Strength;
            v2f vert(appdata v)
            {
                v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o;
            }
            float Hash(float x) { return frac(sin(x * 127.1) * 43758.5453); }
            half3 SampleCapture(float2 uv)
            {
                uv = saturate(uv);
                uv.y = lerp(uv.y, 1 - uv.y, _FlipY);
                return tex2D(_MainTex, uv).rgb;
            }
            half4 frag(v2f i) : SV_Target
            {
                float p = saturate(_Progress);
                float gain = smoothstep(0.02, 0.88, p);
                float2 d = (i.uv - _Center.xy) * float2(_Center.z, 1);
                float r = length(d);
                float2 direction = d / max(r, 0.0001);
                float ripple = sin(r * 43 - p * 17) + 0.45 * sin(r * 71 - p * 24);
                float2 uv = i.uv + direction / float2(_Center.z, 1) * ripple * gain * _Strength.x;
                float twist = gain * 0.09 * exp(-r * 3);
                float s = sin(twist), c = cos(twist);
                float2 spun = float2(c*d.x - s*d.y, s*d.x + c*d.y) / float2(_Center.z, 1);
                uv += spun - (i.uv - _Center.xy);
                uv = _Center.xy + (uv - _Center.xy) * (1 - gain * 0.13);
                float blur = gain * gain * _Strength.y;
                float2 radial = (uv - _Center.xy) * gain * gain * 0.085;
                half3 color = SampleCapture(uv) * 0.2;
                // 八个方向叠加径向偏移，边缘逐渐拉开，中心保留辨识度。
                [unroll] for (int k = 0; k < 8; k++)
                {
                    float angle = k * 0.78539816;
                    float2 tap = float2(cos(angle) / _Center.z, sin(angle)) * blur;
                    color += SampleCapture(uv + tap + radial * (k / 7.0 - 0.5)) * 0.1;
                }
                float angle01 = (atan2(d.y, d.x) + 3.14159265) / 6.2831853;
                float sector = floor(angle01 * 116);
                float lane = frac(angle01 * 116);
                float seed = Hash(sector);
                float width = lerp(0.018, 0.09, Hash(sector + 31));
                float streak = 1 - smoothstep(width, width + 0.06, abs(lane - 0.5));
                float speed = smoothstep(0.32, 0.72, p);
                float travel = frac(r * 1.6 - p * 2.2 + seed);
                streak *= step(0.48, seed) * smoothstep(0.25, 0.68, r)
                    * smoothstep(0.12, 0.6, travel) * speed * _Strength.z;
                color = lerp(color, half3(0.86, 0.98, 1), streak);
                float cover = smoothstep(0.79, 1, p);
                color *= 1 - cover;
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
