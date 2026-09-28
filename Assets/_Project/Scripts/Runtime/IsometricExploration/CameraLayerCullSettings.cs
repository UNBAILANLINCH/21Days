// 职责：一组渲染层及其剔除距离；现有相机配置没有分层条目，独立序列化块供配置列表复用。
using System;
using UnityEngine;

namespace Game.IsometricExploration
{
    [Serializable]
    public sealed class CameraLayerCullSettings
    {
        [SerializeField, Tooltip("应用距离的渲染层；不要为剔除而移动物理碰撞根节点")]
        private LayerMask layers;
        [SerializeField, Min(0f), Tooltip("米；0 使用相机 Far Clip，重复层以后面的条目为准")]
        private float distance;

        public int Layers => layers.value;
        public float Distance => distance > 0f && !float.IsInfinity(distance) ? distance : 0f;

        public CameraLayerCullSettings(int layers, float distance)
        {
            this.layers = layers;
            this.distance = distance;
        }
    }
}
