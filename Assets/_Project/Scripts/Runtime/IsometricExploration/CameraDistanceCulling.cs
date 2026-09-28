// 职责：应用 Unity 原生分层距离剔除并在禁用时恢复。SmoothCameraFollow 只负责位移，
// SceneOccluder 只负责遮挡淡出；二者都不适合承载相机剔除生命周期，因此单独组件。
using System.Collections.Generic;
using UnityEngine;

namespace Game.IsometricExploration
{
    [DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    public sealed class CameraDistanceCulling : MonoBehaviour
    {
        [SerializeField] private IsometricExplorationConfig config;
        private Camera targetCamera;
        private float[] originalDistances;

        private void Awake() => targetCamera = GetComponent<Camera>();
        private void OnEnable() => ApplyConfiguration();

        [ContextMenu("应用距离剔除配置")]
        public void ApplyConfiguration() => ApplySettings(config == null ? null : config.CameraLayerCulling);

        /// <summary>重新应用配置；未指定层恢复启用前的值，不修改配置资产或物体活动状态。</summary>
        public void ApplySettings(IReadOnlyList<CameraLayerCullSettings> settings)
        {
            if (!isActiveAndEnabled) return;
            if (targetCamera == null) targetCamera = GetComponent<Camera>();
            if (originalDistances == null) originalDistances = targetCamera.layerCullDistances;
            var distances = (float[])originalDistances.Clone();
            if (settings != null)
            {
                for (int i = 0; i < settings.Count; i++)
                {
                    CameraLayerCullSettings entry = settings[i];
                    if (entry == null) continue;
                    for (int layer = 0; layer < distances.Length; layer++)
                        if ((entry.Layers & (1 << layer)) != 0) distances[layer] = entry.Distance;
                }
            }
            targetCamera.layerCullDistances = distances;
        }

        private void OnDisable()
        {
            if (targetCamera != null && originalDistances != null)
                targetCamera.layerCullDistances = originalDistances;
            originalDistances = null;
        }
    }
}
