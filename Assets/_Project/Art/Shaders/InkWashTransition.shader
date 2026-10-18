// 职责：连续浓度纹理上的薄墨扩散、积墨和消散；白底仅作数据，不参与输出颜色。
// 新建原因：保留第一版几何遮罩；复用原预览进度驱动，独立比较连续灰阶表现，不改正式转场。
Shader "21Days/UI/InkWashTransition"
{
    Properties
    {
        [PerRendererData] _MainTex ("UI texture", 2D) = "white" {}
        _InkDensityTex ("Ink density data", 2D) = "white" {}
        _Progress ("Progress", Range(0,1)) = 0
        _Aspect ("Screen aspect", Float) = 1.777778
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
            sampler2D _InkDensityTex;
            float _Progress, _Aspect;

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

            half4 frag(v2f i) : SV_Target
            {
                float p = saturate(_Progress);
                if (p <= 0 || p >= 1) return half4(0,0,0,0);

                // 固定 UV 保留洗染层与沉积边，纹理没有缩放生长或逐帧漂动。
                float luminance = dot(tex2D(_InkDensityTex,i.uv).rgb,float3(0.2126,0.7152,0.0722));
                float density = saturate((1-luminance-0.012)/0.988);
                float2 q = (i.uv-0.5)*float2(max(0.1,_Aspect),1);
                float low = Noise(q*2.1+4.7);
                float arrival = length(q*float2(0.68,1))
                    / max(0.1,length(float2(_Aspect*0.5*0.68,0.5)));
                arrival += (low-0.5)*0.16;

                float grow = saturate(p/0.48);
                float dry = saturate((p-0.52)/0.48);
                float front = grow*1.55-0.17;
                float thinFront = smoothstep(arrival-0.24,arrival+0.24,front);
                float depositFront = smoothstep(arrival+0.08,arrival+0.36,front);

                // 积墨先退，薄灰后散；不同区域轻微错时，避免原形整块缩回。
                float localDry = saturate(dry+(Noise(q*1.7+19.2)-0.5)*0.16*sin(dry*3.14159265));
                float depositRemain = 1-smoothstep(0,0.64,localDry);
                float thinRemain = 1-smoothstep(0.12,1,localDry);
                float opticalDepth = 0.75*pow(density,0.62)*thinFront*thinRemain
                    +2.4*density*depositFront*depositRemain;
                float alpha = 1-exp(-opticalDepth);
                float tone = lerp(0.28,0.06,pow(density,0.7));

                // 仅末拍补足遮盖，短暂全遮仍保留整幅墨面浓淡，不输出纯黑块。
                float seal = smoothstep(0.84,1,grow)*(1-smoothstep(0,0.28,dry));
                alpha = lerp(alpha,1,seal);
                return half4(tone,tone,tone,alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
