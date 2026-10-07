// 职责：场景专用暗雾入口与减雾椭球。UI 暗角没有深度；相机剔除只负责渲染距离，均不能扩展成空间雾。
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Game.Core.Simulation;

namespace Game.IsometricExploration
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class DarkFogRegion : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private DarkFogConfig config;
        [SerializeField] private Shader fogShader;
        [SerializeField, Tooltip("编辑时也显示效果；仅当前场景的 Scene 相机。")]
        private bool previewInSceneView = true;
        private Material material;
        private FogPass pass;
        public DarkFogConfig Config => config;

        private void OnEnable() => RenderPipelineManager.beginCameraRendering += BeginCamera;
        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            if (material != null)
            {
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
            material = null;
            pass = null;
        }

        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (targetCamera == null || config == null || fogShader == null || !fogShader.isSupported) return;
            bool scenePreview = previewInSceneView && camera.cameraType == CameraType.SceneView
                && targetCamera.gameObject.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (camera != targetCamera && !scenePreview) return;
            if (material == null) material = new Material(fogShader) { hideFlags = HideFlags.HideAndDontSave };
            if (pass == null) pass = new FogPass(material);
            material.SetColor("_FogColor", config.Color);
            material.SetVector("_FogParams", new Vector4(config.Density, config.MaximumDistance, config.Steps, config.Feather));
            material.SetVector("_FogNoise", new Vector4(config.NoiseStrength, config.NoiseScale, 0f, 0f));
            material.SetMatrix("_FogWorldToRegion", transform.worldToLocalMatrix);
            camera.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(pass);
        }

        public static float DensityMask(float normalizedRadius, float feather)
        {
            float edge = GameMath.Clamp(feather, 0.01f, 1f);
            float t = GameMath.Clamp01((normalizedRadius - (1f - edge)) / edge);
            return t * t * (3f - 2f * t);
        }

        // ponytail: 单区单次全分辨率绘制；先测 GPU，超预算再增加低分辨率缓冲与深度感知上采样。
        private sealed class FogPass : ScriptableRenderPass
        {
            private readonly Material fog;
            public FogPass(Material fog)
            {
                this.fog = fog;
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                ConfigureTarget(renderingData.cameraData.renderer.cameraColorTargetHandle);
                ConfigureClear(ClearFlag.None, Color.clear);
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var cmd = CommandBufferPool.Get("Dark Fog Prototype");
                cmd.DrawProcedural(Matrix4x4.identity, fog, 0, MeshTopology.Triangles, 3);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }
    }
}
