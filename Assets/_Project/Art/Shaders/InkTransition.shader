// 职责：中心渗墨与宽墨流组成的全屏遮罩；纯进度采样，暂停与回拖时纹理不漂移。
// 新建原因：标题水波只扭曲截图、加载黑幕只有均匀透明度，均不能表示水墨形状；不增加渲染管线功能。
Shader "21Days/UI/InkTransition"
{
    Properties
    {
        [PerRendererData] _MainTex ("UI texture", 2D) = "white" {}
        _Progress ("Progress", Range(0,1)) = 0
        _Aspect ("Screen aspect", Float) = 1.777778
        _InkColor ("Dense ink", Color) = (0.009,0.011,0.01,1)
        _FringeColor ("Diluted edge", Color) = (0.19,0.20,0.18,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
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
            float _Progress, _Aspect;
            half4 _InkColor, _FringeColor;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34,456.21));
                p += dot(p,p+45.32);
                return frac(p.x*p.y);
            }

            float Noise(float2 p)
            {
                float2 cell = floor(p), f = frac(p);
                f = f*f*(3-2*f);
                return lerp(lerp(Hash(cell),Hash(cell+float2(1,0)),f.x),
                    lerp(Hash(cell+float2(0,1)),Hash(cell+1),f.x),f.y);
            }

            float InkNoise(float2 p)
            {
                return Noise(p)*0.57+Noise(p*2.03+7.1)*0.29+Noise(p*4.13+19.7)*0.14;
            }

            half4 frag(v2f i) : SV_Target
            {
                float p = saturate(_Progress);
                // 端点与换图平台明确保证透明／全遮，不依赖噪声或屏幕长宽比。
                if (p <= 0 || p >= 1) return half4(0,0,0,0);
                if (p >= 0.44 && p <= 0.56) return half4(_InkColor.rgb,1);
                float growth = p < 0.44 ? p/0.44 : (1-p)/0.44;
                float k = smoothstep(0,1,growth);
                float aspect = max(0.1,_Aspect);
                float2 q = (i.uv-0.5)*float2(aspect,1);
                float corner = length(float2(aspect,1)*0.5);

                float2 warp = float2(InkNoise(q*4.1+2.4),InkNoise(q*4.1+17.8))-0.5;
                float2 w = q+warp*0.16;
                float u = dot(w,float2(0.904,0.428));
                float v = dot(w,float2(-0.428,0.904));
                float grain = InkNoise(q*15.7+4.8)-0.5;
                float radius = lerp(-0.08,corner+0.24,k);
                float core = radius-length(w*float2(1.13,0.93)) + grain*0.115;

                // 两条宽墨流先后铺开，与中心墨团叠合，避免成为规则圆形扩张。
                float flow1 = min(k*(corner+0.8)-abs(u-0.22*k),
                    0.21*k-0.012-abs(v-0.075*sin(u*3.2)-warp.x*0.09));
                float flow2 = min(k*(corner+0.3)-abs(v+0.18*k),
                    0.13*k-0.028-abs(u+0.19*sin(v*2.8))) + grain*0.045;
                float field = max(core,max(flow1,flow2));
                float fibers = (Noise(float2(u*155,v*620))-0.5)*0.014
                    +(Noise(q*287+3.6)-0.5)*0.009;
                float edge = 0.0035+Noise(q*49)*0.003;
                float alpha = smoothstep(-edge,edge,field+fibers);
                float density = smoothstep(0,0.024,field);
                half3 color = lerp(_FringeColor.rgb,_InkColor.rgb,density);
                alpha *= lerp(0.78,1,density);
                // 最后一拍压实四角，遮罩与中段全遮连续衔接。
                float seal = smoothstep(0.88,1,growth);
                color = lerp(color,_InkColor.rgb,seal);
                alpha = lerp(alpha,1,seal);
                return half4(color,alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
