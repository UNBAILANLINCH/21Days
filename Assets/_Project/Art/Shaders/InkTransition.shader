// 职责：从当前角色投影附近向外薄墨渗染，完全遮蔽后就地淡散；只驱动独立预览。
// 新建原因：标题水波只扭曲截图、加载黑幕只有均匀透明度，均不能表示水墨形状；不增加渲染管线功能。
Shader "21Days/UI/InkTransition"
{
    Properties
    {
        [PerRendererData] _MainTex ("UI texture", 2D) = "white" {}
        _Progress ("Progress", Range(0,1)) = 0
        _Aspect ("Screen aspect", Float) = 1.777778
        _OriginUV ("Actor origin UV", Vector) = (0.5,0.5,0,0)
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
            float4 _OriginUV;
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
                half3 inkColor = _InkColor.rgb;
                if (p <= 0 || p >= 1) return half4(0,0,0,0);
                if (p >= 0.44 && p <= 0.56) return half4(inkColor,1);
                float aspect = clamp(_Aspect,0.1,8);
                float2 origin = saturate(_OriginUV.xy);
                float2 q = (i.uv-origin)*float2(aspect,1);
                // 到达场来自角色投影，不是屏幕一侧或固定中心；低频渗染不逐帧随机。
                float n = Noise(q*3.8+12.1)*0.62 + Noise(q*8.1+28.4)*0.28 + Noise(q*19+7.7)*0.10;
                float2 warped = q*float2(1+(n-0.5)*0.7,1-(n-0.5)*0.5);
                float arrival = max(0,length(warped)*(0.72+n*0.68)+(n-0.5)*0.07);
                float alpha;
                if (p < 0.44)
                {
                    // 首拍留给角色本体的小墨痕；之后薄墨向周围润开，末段才压实遮屏。
                    float growth = saturate((p-0.18)/0.26);
                    float2 furthest = max(origin,1-origin)*float2(aspect,1);
                    float reach = (length(furthest)*1.6+0.1)*pow(growth,2.25)-0.055;
                    float wet = smoothstep(-0.045,0.035,reach-arrival);
                    float dense = smoothstep(0.025,0.16,reach-arrival);
                    alpha = lerp(wet*(0.22+n*0.18),0.62+n*0.18,dense);
                    alpha = lerp(alpha,1,smoothstep(0.72,1,growth));
                }
                else
                {
                    // 全遮后就地淡散，不把墨团缩回角色，不引入额外角色动作。
                    float dry = (p-0.56)/0.44;
                    float localDry = saturate(dry+(n-0.5)*0.36*sin(dry*3.14159265));
                    alpha = 1-smoothstep(0,1,localDry);
                }
                return half4(inkColor,alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
