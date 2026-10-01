// 暗雾原型：现有 Sprite 雾没有玩家周围空间减雾，因此独立全屏通道。
Shader "21Days/DarkFog"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            float4 _FogColor, _FogParams, _FogNoise;
            float4x4 _FogWorldToRegion;
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(uint id : SV_VertexID)
            {
                Varyings o;
                o.positionCS = GetFullScreenTriangleVertexPosition(id);
                o.uv = GetFullScreenTriangleTexCoord(id);
                return o;
            }
            float Mask(float3 p)
            {
                float r = length(mul(_FogWorldToRegion, float4(p, 1)).xyz);
                return smoothstep(1.0 - _FogParams.w, 1.0, r);
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float depth = SampleSceneDepth(input.uv);
                #if !UNITY_REVERSED_Z
                    depth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, depth);
                #endif
                float3 surface = ComputeWorldSpacePosition(input.uv, depth, UNITY_MATRIX_I_VP);
                float surfaceMask = Mask(surface);
                if (surfaceMask <= 0.0 || _FogParams.x <= 0.0) return half4(0, 0, 0, 0);
                float3 origin = GetCameraPositionWS();
                float3 ray = surface - origin;
                float distanceToSurface = length(ray);
                float3 direction = ray / max(distanceToSurface, 0.0001);
                if (unity_OrthoParams.w > 0.5)
                {
                    direction = -UNITY_MATRIX_V[2].xyz;
                    distanceToSurface = max(0.0, dot(ray, direction));
                    origin = surface - direction * distanceToSurface;
                }
                float travel = min(distanceToSurface, _FogParams.y);
                int steps = (int)_FogParams.z;
                float stepLength = travel / steps;
                float opticalDepth = 0.0;
                [loop] for (int i = 0; i < steps; ++i)
                {
                    float3 p = origin + direction * ((i + 0.5) * stepLength);
                    float3 n = p * _FogNoise.y + _Time.y * float3(0.08, 0.02, 0.05);
                    float noise = sin(n.x) * sin(n.y + 1.7) * sin(n.z + 0.6);
                    opticalDepth += Mask(p) * (1.0 + noise * _FogNoise.x) * stepLength;
                    if (opticalDepth > 8.0) break;
                }
                // ponytail: 表面清晰权重保障远置相机仍看得见主角；属于玩法显现效果，不是物理散射。
                float opacity = (1.0 - exp(-opticalDepth)) * surfaceMask * saturate(_FogParams.x);
                return half4(_FogColor.rgb, opacity);
            }
            ENDHLSL
        }
    }
}
